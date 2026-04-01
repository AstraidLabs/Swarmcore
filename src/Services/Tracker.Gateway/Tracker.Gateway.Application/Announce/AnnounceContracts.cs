using Microsoft.AspNetCore.Http;
using BeeTracker.Contracts.Configuration;

namespace Tracker.Gateway.Application.Announce;

public interface IAnnounceRequestParser
{
    bool TryParse(HttpContext httpContext, string? passkey, out AnnounceRequest request, out AnnounceError error);
}

public interface IAnnounceRequestValidator
{
    AnnounceValidationResult Validate(in AnnounceRequest request);
}

public interface IAccessPolicyResolver
{
    ValueTask<AccessResolution> ResolveAsync(AnnounceRequest request, CancellationToken cancellationToken);
}

public interface IRuntimeSwarmStore
{
    SwarmCounts ApplyMutation(in AnnounceRequest request, TimeSpan peerTtl, DateTimeOffset now);
    AnnouncePeerSelection SelectPeers(in AnnounceRequest request, int peerCount, DateTimeOffset now);
    SwarmCounts GetCounts(in InfoHashKey infoHash, DateTimeOffset now);
    void SweepExpired(DateTimeOffset now);

    /// <summary>
    /// Enumerates all swarms currently active in the local runtime store with their counts.
    /// Used for distributed summary publication and observability.
    /// The snapshot is taken at the time of the call; the DateTimeOffset is used to expire stale peers.
    /// </summary>
    IEnumerable<(InfoHashKey InfoHash, SwarmCounts Counts)> EnumerateSwarms(DateTimeOffset now);
}

public interface IPeerMutationService
{
    SwarmCounts Apply(in AnnounceRequest request, int announceIntervalSeconds, DateTimeOffset now);
}

public interface IPeerSelectionService
{
    AnnouncePeerSelection Select(in AnnounceRequest request, int requestedPeers, int maxPeers, DateTimeOffset now);
}

public interface IAnnounceTelemetryWriter
{
    bool TryWrite(AnnounceTelemetryRecord telemetryRecord);
    int QueueLength { get; }
}

public interface IAnnounceService
{
    ValueTask<(AnnounceSuccess? Success, AnnounceError? Error)> ExecuteAsync(AnnounceRequest request, CancellationToken cancellationToken);
}

public interface IScrapeRequestParser
{
    bool TryParse(HttpContext httpContext, string? passkey, out ScrapeRequest request, out AnnounceError error);
}

public interface IScrapeRequestValidator
{
    AnnounceValidationResult Validate(in ScrapeRequest request);
}

public interface IScrapeAccessPolicyResolver
{
    ValueTask<ScrapeAccessResolution> ResolveAsync(string? passkey, InfoHashKey infoHash, CancellationToken cancellationToken);
}

public interface IScrapeService
{
    ValueTask<(ScrapeSuccess? Success, AnnounceError? Error)> ExecuteAsync(ScrapeRequest request, CancellationToken cancellationToken);
}

public interface IPasskeyRedactor
{
    string? Redact(string? passkey);
}

public interface IIpBanGuard
{
    ValueTask<AnnounceError?> EvaluateAsync(string ip, CancellationToken cancellationToken);
}

public interface IAnnounceAbuseGuard
{
    AnnounceError? Evaluate(HttpContext httpContext, in AnnounceRequest request);
}

public interface IScrapeAbuseGuard
{
    AnnounceError? Evaluate(HttpContext httpContext);
}

public interface IAccessSnapshotProvider
{
    ValueTask<TorrentPolicyDto?> GetTorrentPolicyAsync(string infoHashHex, CancellationToken cancellationToken);
    ValueTask<PasskeyAccessDto?> GetPasskeyAsync(string passkey, CancellationToken cancellationToken);
    ValueTask<TrackerAccessRightsDto?> GetTrackerAccessRightsAsync(Guid userId, CancellationToken cancellationToken);
    ValueTask<BanRuleDto?> GetBanRuleAsync(string scope, string subject, CancellationToken cancellationToken);
}

#pragma warning disable CS0618
public static class AccessSnapshotProviderExtensions
{
    public static async ValueTask<UserPermissionSnapshotDto?> GetUserPermissionAsync(
        this IAccessSnapshotProvider provider,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var rights = await provider.GetTrackerAccessRightsAsync(userId, cancellationToken);
        return rights?.ToSnapshot();
    }
}
#pragma warning restore CS0618
