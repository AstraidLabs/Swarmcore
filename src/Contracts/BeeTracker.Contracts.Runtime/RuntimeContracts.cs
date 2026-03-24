namespace BeeTracker.Contracts.Runtime;

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

// ─── Cluster / Distributed Runtime ───────────────────────────────────────────

/// <summary>
/// Represents the operational lifecycle state of a tracker node.
/// Used to implement drain and maintenance workflows.
/// </summary>
public enum NodeOperationalState
{
    /// <summary>Node is fully active and accepting all traffic.</summary>
    Active = 0,

    /// <summary>
    /// Node is draining: no new shard ownership is claimed, existing leases are released
    /// gracefully, and the readiness probe fails so the load balancer stops routing new requests.
    /// </summary>
    Draining = 1,

    /// <summary>
    /// Node is offline for maintenance. Behaves like Draining but signals a planned outage.
    /// </summary>
    Maintenance = 2
}

/// <summary>
/// Persistent record of ownership for a single cluster shard, stored in Redis.
/// A shard is owned by at most one node at a time. Epoch is incremented on each new claim.
/// </summary>
public sealed record ShardOwnershipRecord(
    int ShardId,
    string OwnerNodeId,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset LeaseExpiresAtUtc,
    long Epoch);

/// <summary>
/// Current operational state of a node, stored in Redis.
/// Nodes publish this on state transitions and on heartbeat refresh.
/// </summary>
public sealed record NodeOperationalStateDto(
    string NodeId,
    NodeOperationalState State,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Distributed swarm summary published by the owning gateway node to Redis.
/// Enables non-owning nodes and the admin service to observe cluster-wide swarm health
/// without accessing the owning node directly.
/// </summary>
public sealed record SwarmSummaryDto(
    string InfoHash,
    string OwnerNodeId,
    int SeederCount,
    int LeecherCount,
    int CompletedCount,
    DateTimeOffset PublishedAtUtc);

/// <summary>
/// Snapshot of a single cluster shard's ownership state as seen by this node.
/// </summary>
public sealed record ClusterShardStateDto(
    int ShardId,
    string? OwnerNodeId,
    bool IsOwnedLocally,
    DateTimeOffset? LeaseExpiresAtUtc);

/// <summary>
/// Cluster-level view of a single node combining heartbeat and operational state.
/// </summary>
public sealed record NodeClusterStateDto(
    string NodeId,
    string Region,
    NodeOperationalState OperationalState,
    int OwnedShardCount,
    DateTimeOffset HeartbeatObservedAtUtc,
    bool HeartbeatFresh);
