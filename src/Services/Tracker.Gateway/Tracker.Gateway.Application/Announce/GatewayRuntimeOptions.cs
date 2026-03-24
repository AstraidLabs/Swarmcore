namespace Tracker.Gateway.Application.Announce;

public sealed class GatewayRuntimeOptions
{
    public const string SectionName = "BeeTracker:GatewayRuntime";

    public int ShardCount { get; init; } = 64;
    public int MaxPeersPerResponse { get; init; } = 80;
    public int PeerTtlSeconds { get; init; } = 2700;
    public int ExpirySweepIntervalSeconds { get; init; } = 30;
}
