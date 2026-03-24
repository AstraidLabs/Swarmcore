using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using Swarmcore.BuildingBlocks.Abstractions.Time;
using Swarmcore.BuildingBlocks.Observability.Diagnostics;

namespace Tracker.Gateway.Application.Announce;

public sealed class AnnounceService(
    IAccessPolicyResolver accessPolicyResolver,
    IPeerMutationService peerMutationService,
    IPeerSelectionService peerSelectionService,
    IAnnounceTelemetryWriter telemetryWriter,
    IPasskeyRedactor passkeyRedactor,
    IClock clock,
    IOptions<GatewayRuntimeOptions> runtimeOptions,
    IOptions<TrackerNodeOptions> nodeOptions,
    IRuntimeGovernanceState governanceState) : IAnnounceService
{
    public async ValueTask<(AnnounceSuccess? Success, AnnounceError? Error)> ExecuteAsync(AnnounceRequest request, CancellationToken cancellationToken)
    {
        // ── Governance checks (no allocations on hot path) ──
        if (governanceState.GlobalMaintenanceMode)
        {
            TrackerDiagnostics.GovernanceMaintenanceRejected.Add(1);
            return (null, new AnnounceError(StatusCodes.Status503ServiceUnavailable, "tracker is in maintenance mode"));
        }

        if (governanceState.AnnounceDisabled)
        {
            TrackerDiagnostics.GovernanceAnnounceRejected.Add(1);
            return (null, new AnnounceError(StatusCodes.Status503ServiceUnavailable, "announce is temporarily disabled"));
        }

        var accessResolution = await accessPolicyResolver.ResolveAsync(request, cancellationToken);
        if (!accessResolution.IsAllowed)
        {
            return (null, new AnnounceError(StatusCodes.Status403Forbidden, accessResolution.FailureReason));
        }

        var policy = accessResolution.Policy;

        // ── Tracker ID soft validation ──
        if (request.TrackerId is not null)
        {
            var expectedTrackerId = GenerateTrackerId(nodeOptions.Value.NodeId, request.InfoHash, request.PeerId);
            if (!string.Equals(request.TrackerId, expectedTrackerId, StringComparison.Ordinal))
            {
                TrackerDiagnostics.CompatibilityWarningIssued.Add(1);
            }
        }

        // ── Per-torrent governance overrides ──
        if (policy.MaintenanceFlag)
        {
            TrackerDiagnostics.TorrentMaintenanceRejected.Add(1);
            return (null, new AnnounceError(StatusCodes.Status503ServiceUnavailable, "torrent is under maintenance"));
        }

        if (policy.TemporaryRestriction)
        {
            TrackerDiagnostics.TorrentTemporaryRestrictionRejected.Add(1);
            return (null, new AnnounceError(StatusCodes.Status403Forbidden, "torrent is temporarily restricted"));
        }

        // ── Per-torrent compact enforcement ──
        if (policy.CompactOnly && !request.Compact)
        {
            var profile = EffectiveProtocolProfile.Resolve(governanceState, policy);
            if (profile.CompatibilityMode != ClientCompatibilityMode.Compatibility)
            {
                return (null, new AnnounceError(StatusCodes.Status400BadRequest, "only compact responses are supported for this torrent"));
            }
            TrackerDiagnostics.CompatibilityFallback.Add(1, new KeyValuePair<string, object?>("field", "torrent_compact"));
        }

        // ── Per-torrent IPv6 check ──
        if (!policy.AllowIPv6 && request.Endpoint.AddressFamily == PeerAddressFamily.IPv6)
        {
            return (null, new AnnounceError(StatusCodes.Status400BadRequest, "IPv6 is not allowed for this torrent"));
        }

        // ── Global IPv6 freeze check ──
        if (governanceState.IPv6Frozen && request.Endpoint.AddressFamily == PeerAddressFamily.IPv6)
        {
            return (null, new AnnounceError(StatusCodes.Status400BadRequest, "IPv6 peer registration is temporarily frozen"));
        }

        // ── Per-torrent override tracking ──
        if (policy.StrictnessProfileOverride.HasValue || policy.CompatibilityModeOverride.HasValue)
        {
            TrackerDiagnostics.TorrentOverrideApplied.Add(1);
        }

        var now = clock.UtcNow;

        // ── Read-only mode: skip mutation but still return peers ──
        SwarmCounts counts;
        if (governanceState.ReadOnlyMode)
        {
            TrackerDiagnostics.GovernanceReadOnlySkipped.Add(1);
            counts = default;
        }
        else
        {
            counts = peerMutationService.Apply(request, policy.AnnounceIntervalSeconds, now);
        }

        var effectiveMaxPeers = Math.Min(runtimeOptions.Value.MaxPeersPerResponse, policy.MaxNumWant);
        var selection = request.Event == TrackerEvent.Stopped
            ? default
            : peerSelectionService.Select(request, request.RequestedPeers, effectiveMaxPeers, now);

        // ── Build warning message ──
        string? warningMessage = policy.WarningMessage;
        if (policy.ModerationState is not null && warningMessage is null)
        {
            warningMessage = policy.ModerationState switch
            {
                "review" => "This torrent is under review.",
                "flagged" => "This torrent has been flagged for policy review.",
                _ => null
            };
            if (warningMessage is not null)
            {
                TrackerDiagnostics.CompatibilityWarningIssued.Add(1);
            }
        }

        telemetryWriter.TryWrite(new AnnounceTelemetryRecord(
            nodeOptions.Value.NodeId,
            request.InfoHash.ToHexString(),
            request.PeerId.ToHexString(),
            passkeyRedactor.Redact(request.Passkey),
            request.Event,
            request.RequestedPeers,
            selection.Peers.Count + selection.Peers6.Count,
            now));

        // In compatibility mode, force compact=true when the client requested non-compact
        // but the policy requires compact-only.
        var effectiveCompact = request.Compact || policy.CompactOnly;

        // Generate a deterministic tracker ID for the peer session.
        // This allows the client to echo it back in subsequent announces for session continuity.
        // Derived from node identity + info hash + peer ID so it is stable across announces
        // from the same peer on the same node.
        var trackerId = GenerateTrackerId(nodeOptions.Value.NodeId, request.InfoHash, request.PeerId);

        var minInterval = Math.Max(60, policy.AnnounceIntervalSeconds / 2);

        return (new AnnounceSuccess(
            policy.AnnounceIntervalSeconds,
            counts.SeederCount,
            counts.LeecherCount,
            selection,
            WarningMessage: warningMessage,
            Compact: effectiveCompact,
            NoPeerId: request.NoPeerId,
            TrackerId: trackerId,
            MinIntervalSeconds: minInterval), null);
    }

    private static string GenerateTrackerId(string nodeId, InfoHashKey infoHash, PeerIdKey peerId)
    {
        // Deterministic tracker ID: SHA256(nodeId + infoHash + peerId) truncated to 20 hex chars.
        // Stable for the same peer on the same node, changes if the peer moves to a different node.
        // Uses SHA256.HashData with a single stackalloc to avoid IDisposable allocation on the hot path.
        Span<byte> buffer = stackalloc byte[256 + 40];
        var nodeLen = Encoding.UTF8.GetBytes(nodeId, buffer);
        infoHash.WriteBytes(buffer.Slice(nodeLen, 20));
        peerId.WriteBytes(buffer.Slice(nodeLen + 20, 20));
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(buffer[..(nodeLen + 40)], hash);
        Span<char> hex = stackalloc char[20];
        Convert.TryToHexString(hash[..10], hex, out _);
        return new string(hex);
    }
}

public sealed class ScrapeService(
    IScrapeAccessPolicyResolver accessPolicyResolver,
    IRuntimeSwarmStore runtimeSwarmStore,
    IRuntimeGovernanceState governanceState,
    IClock clock) : IScrapeService
{
    public async ValueTask<(ScrapeSuccess? Success, AnnounceError? Error)> ExecuteAsync(ScrapeRequest request, CancellationToken cancellationToken)
    {
        if (governanceState.GlobalMaintenanceMode)
        {
            TrackerDiagnostics.GovernanceMaintenanceRejected.Add(1);
            return (null, new AnnounceError(StatusCodes.Status503ServiceUnavailable, "tracker is in maintenance mode"));
        }

        if (governanceState.ScrapeDisabled)
        {
            TrackerDiagnostics.GovernanceScrapeRejected.Add(1);
            return (null, new AnnounceError(StatusCodes.Status503ServiceUnavailable, "scrape is temporarily disabled"));
        }

        var now = clock.UtcNow;
        var files = new List<ScrapeFileEntry>(request.InfoHashes.Length);

        foreach (var infoHash in request.InfoHashes)
        {
            var accessResolution = await accessPolicyResolver.ResolveAsync(request.Passkey, infoHash, cancellationToken);
            if (!accessResolution.IsAllowed)
            {
                continue;
            }

            // Per-torrent scrape check is already in access resolver (AllowScrape).
            // Additional per-torrent governance checks:
            if (accessResolution.Policy is { } scrapePolicy)
            {
                if (scrapePolicy.MaintenanceFlag || scrapePolicy.TemporaryRestriction)
                {
                    continue;
                }
            }

            var counts = runtimeSwarmStore.GetCounts(infoHash, now);
            files.Add(new ScrapeFileEntry(infoHash, counts.SeederCount, counts.LeecherCount, counts.DownloadedCount));
        }

        if (files.Count == 0)
        {
            return (null, new AnnounceError(StatusCodes.Status403Forbidden, "scrape not permitted"));
        }

        return (new ScrapeSuccess(files.ToArray()), null);
    }
}
