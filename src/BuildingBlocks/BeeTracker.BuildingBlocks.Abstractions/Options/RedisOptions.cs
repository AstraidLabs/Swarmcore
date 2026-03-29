namespace BeeTracker.BuildingBlocks.Abstractions.Options;

public sealed class RedisOptions
{
    public const string SectionName = "BeeTracker:Redis";

    public string Configuration { get; init; } = "localhost:6379";
    public string KeyPrefix { get; init; } = "beetracker";
    public int PolicyCacheTtlSeconds { get; init; } = 60;
    public int SnapshotCacheTtlSeconds { get; init; } = 30;
    public string InvalidationChannel { get; init; } = "tracker:config:invalidate";
    public int HeartbeatTtlSeconds { get; init; } = 45;
    public int OwnershipLeaseDurationSeconds { get; init; } = 45;
    public int OwnershipRefreshIntervalSeconds { get; init; } = 15;
    public int SwarmSummaryPublishIntervalSeconds { get; init; } = 30;
    public int SwarmSummaryTtlSeconds { get; init; } = 90;
}
