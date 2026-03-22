namespace Swarmcore.Contracts.Runtime;

public sealed record NodeHeartbeatDto(
    string NodeId,
    string Region,
    DateTimeOffset ObservedAtUtc);

public sealed record NodeRuntimeStatsDto(
    string NodeId,
    int ActiveSwarms,
    int ActivePeers,
    long AnnounceRequestsPerMinute,
    DateTimeOffset ObservedAtUtc);

public sealed record SwarmSnapshotDto(
    string InfoHash,
    int SeederCount,
    int LeecherCount,
    DateTimeOffset ObservedAtUtc);
