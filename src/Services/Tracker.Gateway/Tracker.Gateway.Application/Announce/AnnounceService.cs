using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using Swarmcore.BuildingBlocks.Abstractions.Time;

namespace Tracker.Gateway.Application.Announce;

public sealed class AnnounceService(
    IAccessPolicyResolver accessPolicyResolver,
    IPeerMutationService peerMutationService,
    IPeerSelectionService peerSelectionService,
    IAnnounceTelemetryWriter telemetryWriter,
    IPasskeyRedactor passkeyRedactor,
    IClock clock,
    IOptions<GatewayRuntimeOptions> runtimeOptions,
    IOptions<TrackerNodeOptions> nodeOptions) : IAnnounceService
{
    public async ValueTask<(AnnounceSuccess? Success, AnnounceError? Error)> ExecuteAsync(AnnounceRequest request, CancellationToken cancellationToken)
    {
        var accessResolution = await accessPolicyResolver.ResolveAsync(request, cancellationToken);
        if (!accessResolution.IsAllowed)
        {
            return (null, new AnnounceError(StatusCodes.Status403Forbidden, accessResolution.FailureReason));
        }

        var now = clock.UtcNow;
        var counts = peerMutationService.Apply(request, accessResolution.Policy.AnnounceIntervalSeconds, now);

        var selection = request.Event == TrackerEvent.Stopped
            ? default
            : peerSelectionService.Select(request, request.RequestedPeers, runtimeOptions.Value.MaxPeersPerResponse, now);

        telemetryWriter.TryWrite(new AnnounceTelemetryRecord(
            nodeOptions.Value.NodeId,
            request.InfoHash.ToHexString(),
            request.PeerId.ToHexString(),
            passkeyRedactor.Redact(request.Passkey),
            request.Event,
            request.RequestedPeers,
            selection.Peers.Count + selection.Peers6.Count,
            now));

        return (new AnnounceSuccess(
            accessResolution.Policy.AnnounceIntervalSeconds,
            counts.SeederCount,
            counts.LeecherCount,
            selection,
            WarningMessage: accessResolution.Policy.WarningMessage,
            Compact: request.Compact), null);
    }
}

public sealed class ScrapeService(
    IScrapeAccessPolicyResolver accessPolicyResolver,
    IRuntimeSwarmStore runtimeSwarmStore,
    IClock clock) : IScrapeService
{
    public async ValueTask<(ScrapeSuccess? Success, AnnounceError? Error)> ExecuteAsync(ScrapeRequest request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var files = new List<ScrapeFileEntry>(request.InfoHashes.Length);

        foreach (var infoHash in request.InfoHashes)
        {
            var accessResolution = await accessPolicyResolver.ResolveAsync(request.Passkey, infoHash, cancellationToken);
            if (!accessResolution.IsAllowed)
            {
                continue;
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
