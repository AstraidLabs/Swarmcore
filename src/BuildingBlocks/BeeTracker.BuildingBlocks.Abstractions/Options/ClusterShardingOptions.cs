namespace BeeTracker.BuildingBlocks.Abstractions.Options;

/// <summary>
/// Configuration for the cluster shard ownership and distributed coordination model.
/// All nodes in the cluster must share the same ClusterShardCount value.
/// </summary>
public sealed class ClusterShardingOptions
{
    public const string SectionName = "BeeTracker:ClusterSharding";

    /// <summary>
    /// Number of virtual cluster shards. All nodes must agree on this value.
    /// Changing this value requires a coordinated rolling restart of all nodes.
    /// Default: 256.
    /// </summary>
    public int ClusterShardCount { get; init; } = 256;

    /// <summary>
    /// Duration in seconds that an ownership lease is valid before it must be refreshed.
    /// A node that fails to refresh within this window loses its shard ownership.
    /// Default: 60.
    /// </summary>
    public int OwnershipLeaseDurationSeconds { get; init; } = 60;

    /// <summary>
    /// Interval in seconds at which a node refreshes its ownership leases.
    /// Must be less than OwnershipLeaseDurationSeconds to avoid false expiry.
    /// Default: 20.
    /// </summary>
    public int OwnershipRefreshIntervalSeconds { get; init; } = 20;

    /// <summary>
    /// Interval in seconds at which the gateway refreshes its local ownership cache from Redis.
    /// Lower values mean faster ownership change propagation to routing decisions.
    /// Default: 15.
    /// </summary>
    public int OwnershipCacheRefreshIntervalSeconds { get; init; } = 15;

    /// <summary>
    /// Seconds after which a node with no heartbeat is considered failed and its shards
    /// become reclaimable by the first node to detect the expiry.
    /// Must be greater than the heartbeat interval plus expected clock drift.
    /// Default: 45.
    /// </summary>
    public int FailoverTimeoutSeconds { get; init; } = 45;

    /// <summary>
    /// Interval in seconds at which swarm summaries are published to the distributed cache.
    /// Default: 30.
    /// </summary>
    public int SwarmSummaryPublishIntervalSeconds { get; init; } = 30;

    /// <summary>
    /// TTL in seconds for distributed swarm summary entries in Redis.
    /// Should be at least 2x the SwarmSummaryPublishIntervalSeconds.
    /// Default: 90.
    /// </summary>
    public int SwarmSummaryTtlSeconds { get; init; } = 90;

    /// <summary>
    /// When true, a node in Draining or Maintenance state will fail its readiness probe,
    /// allowing the load balancer to remove it from rotation.
    /// Default: true.
    /// </summary>
    public bool FailReadinessWhenDraining { get; init; } = true;
}
