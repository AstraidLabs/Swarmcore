using Swarmcore.Contracts.Runtime;
using Tracker.Gateway.Application.Announce;

namespace Tracker.Gateway.Application.Cluster;

// ─── Shard Routing ────────────────────────────────────────────────────────────

/// <summary>
/// Maps info_hashes to deterministic cluster shards and exposes local ownership knowledge.
///
/// Architecture: local-first with distributed enrichment.
/// Each gateway node serves all traffic from its local peer store.
/// Cluster shard routing is used to:
///  - report per-shard ownership metrics
///  - annotate telemetry with locality (local-owned vs not)
///  - drive swarm summary publication (owner publishes, others may read)
///
/// Requests to non-locally-owned swarms are NOT redirected or proxied.
/// The load balancer or ingress is responsible for consistent hash routing
/// so that the owning node typically receives the traffic.
/// </summary>
public interface IShardRouter
{
    /// <summary>
    /// Returns the deterministic cluster shard index (0..ClusterShardCount-1) for the given info_hash.
    /// This mapping is stable across all nodes as long as ClusterShardCount is unchanged.
    /// </summary>
    int GetClusterShard(in InfoHashKey infoHash);

    /// <summary>
    /// Returns the node ID that currently owns the given cluster shard,
    /// or null if the shard is unowned or the ownership is not yet known locally.
    /// </summary>
    string? GetOwnerNodeId(int clusterShardId);

    /// <summary>
    /// Returns true if this node currently owns the given cluster shard.
    /// </summary>
    bool IsLocallyOwned(int clusterShardId);

    /// <summary>
    /// Returns true if this node is the owner of the cluster shard for the given info_hash.
    /// Shorthand for IsLocallyOwned(GetClusterShard(infoHash)).
    /// </summary>
    bool IsLocallyOwned(in InfoHashKey infoHash);

    /// <summary>
    /// Returns a point-in-time snapshot of all known shard-to-node assignments.
    /// May contain stale data until the next ownership cache refresh.
    /// </summary>
    IReadOnlyDictionary<int, string> GetOwnershipSnapshot();

    /// <summary>
    /// Total number of cluster shards owned by this node according to the local cache.
    /// </summary>
    int LocallyOwnedShardCount { get; }
}

// ─── Ownership Cache (local in-memory, refreshed from Redis) ──────────────────

/// <summary>
/// Local in-memory cache of shard-to-node ownership assignments.
/// Refreshed periodically from Redis by a background service.
/// Read by IShardRouter; written by the cache refresher background service.
/// </summary>
public interface IShardOwnershipCache
{
    /// <summary>
    /// Returns the owner node ID for the given shard, or null if unknown/unowned.
    /// </summary>
    string? GetOwnerNodeId(int clusterShardId);

    /// <summary>
    /// Returns true if the local node ID owns the given shard.
    /// </summary>
    bool IsLocallyOwned(int clusterShardId);

    /// <summary>
    /// Returns the count of shards currently owned by the local node.
    /// </summary>
    int LocallyOwnedCount { get; }

    /// <summary>
    /// Returns a snapshot of all current shard-to-node assignments.
    /// </summary>
    IReadOnlyDictionary<int, string> GetSnapshot();

    /// <summary>
    /// Replaces the current ownership map with a fresh snapshot from Redis.
    /// Called by the background cache refresher.
    /// </summary>
    void Update(string localNodeId, IReadOnlyDictionary<int, string> ownerships);
}

// ─── Distributed Swarm Summaries ─────────────────────────────────────────────

/// <summary>
/// Publishes and retrieves distributed swarm summaries.
///
/// The owning node for a swarm publishes a summary to Redis periodically.
/// Non-owning nodes can read the summary to return accurate scrape counts.
/// Summaries are eventually consistent and have a configurable TTL.
/// </summary>
public interface ISwarmSummaryStore
{
    /// <summary>
    /// Publishes a swarm summary to the distributed cache.
    /// Should only be called by the node that owns the shard for this info_hash.
    /// </summary>
    Task PublishAsync(SwarmSummaryDto summary, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves the distributed swarm summary for the given info_hash hex string.
    /// Returns null if no summary exists (e.g., swarm has no peers yet, or summary expired).
    /// </summary>
    Task<SwarmSummaryDto?> GetAsync(string infoHashHex, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes summaries for all active swarms in a single batched operation.
    /// </summary>
    Task PublishBatchAsync(IReadOnlyList<SwarmSummaryDto> summaries, CancellationToken cancellationToken);
}

// ─── Node Operational State ───────────────────────────────────────────────────

/// <summary>
/// Reads and publishes node operational state (Active/Draining/Maintenance).
/// Used by drain and maintenance workflows.
/// </summary>
public interface INodeOperationalStateStore
{
    /// <summary>
    /// Returns the current operational state of the specified node,
    /// or null if no state has been published (treated as Active).
    /// </summary>
    Task<NodeOperationalStateDto?> GetStateAsync(string nodeId, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the operational state for the specified node.
    /// </summary>
    Task SetStateAsync(NodeOperationalStateDto state, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the operational states of all known nodes.
    /// </summary>
    Task<IReadOnlyCollection<NodeOperationalStateDto>> GetAllStatesAsync(CancellationToken cancellationToken);
}
