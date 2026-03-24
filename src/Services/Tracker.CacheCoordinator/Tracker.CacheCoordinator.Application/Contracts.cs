using BeeTracker.Contracts.Runtime;

namespace Tracker.CacheCoordinator.Application;

public interface INodeHeartbeatRegistry
{
    Task PublishHeartbeatAsync(NodeHeartbeatDto heartbeat, CancellationToken cancellationToken);
}

// ─── Shard Ownership Coordination ────────────────────────────────────────────

/// <summary>
/// Manages shard ownership leases in the distributed coordination layer (Redis).
/// Implemented by the CacheCoordinator; read by gateways via IShardOwnershipCache.
/// </summary>
public interface IShardOwnershipRegistry
{
    /// <summary>
    /// Attempts to claim ownership of a cluster shard.
    /// Uses Redis SET NX (set-if-not-exists) semantics so only one node succeeds.
    /// Returns the resulting ownership record (this node's or the existing owner's).
    /// </summary>
    Task<ShardOwnershipRecord?> TryClaimAsync(int shardId, string nodeId, TimeSpan leaseDuration, CancellationToken cancellationToken);

    /// <summary>
    /// Refreshes the TTL on an already-owned shard lease, incrementing nothing.
    /// Returns false if the lease no longer belongs to this node (e.g., expired and reclaimed).
    /// </summary>
    Task<bool> TryRefreshAsync(int shardId, string nodeId, TimeSpan leaseDuration, CancellationToken cancellationToken);

    /// <summary>
    /// Releases ownership of a shard. Used during graceful drain/shutdown.
    /// Only releases if the current owner matches nodeId (compare-and-delete).
    /// </summary>
    Task<bool> TryReleaseAsync(int shardId, string nodeId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the current ownership record for the given shard, or null if unowned/expired.
    /// </summary>
    Task<ShardOwnershipRecord?> GetOwnershipAsync(int shardId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns ownership records for all shards (only shards with active leases are returned).
    /// </summary>
    Task<IReadOnlyDictionary<int, ShardOwnershipRecord>> GetAllOwnershipsAsync(int totalShardCount, CancellationToken cancellationToken);
}

/// <summary>
/// High-level service that manages this node's shard ownership lifecycle:
/// claiming shards on startup, refreshing leases, detecting failovers, and releasing on drain.
/// </summary>
public interface IShardOwnershipService
{
    /// <summary>
    /// Claims all currently unclaimed shards and any shards whose leases have expired
    /// (failover recovery). Returns the number of shards claimed.
    /// </summary>
    Task<int> ClaimAvailableShardsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Refreshes leases for all shards currently owned by this node.
    /// Returns the number of shards whose refresh failed (lost ownership).
    /// </summary>
    Task<int> RefreshOwnedShardsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Releases all shards owned by this node. Used during graceful drain/shutdown.
    /// </summary>
    Task ReleaseAllShardsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns the set of shard IDs currently owned by this node.
    /// </summary>
    IReadOnlySet<int> OwnedShardIds { get; }
}

// ─── Node Operational State ───────────────────────────────────────────────────

/// <summary>
/// Manages the operational state of the local node (Active/Draining/Maintenance).
/// </summary>
public interface INodeOperationalStateService
{
    /// <summary>
    /// Returns the current operational state of the local node.
    /// </summary>
    NodeOperationalState CurrentState { get; }

    /// <summary>
    /// Transitions the local node to the specified state and publishes to Redis.
    /// </summary>
    Task TransitionToAsync(NodeOperationalState newState, CancellationToken cancellationToken);
}
