using System.Collections.Frozen;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using BeeTracker.BuildingBlocks.Abstractions.Options;
using Tracker.Gateway.Application.Announce;
using Tracker.Gateway.Application.Cluster;

namespace Tracker.Gateway.Runtime;

/// <summary>
/// Deterministic cluster shard router.
///
/// Shard assignment algorithm:
///   clusterShardId = (first 4 bytes of info_hash, read as big-endian uint32) % ClusterShardCount
///
/// This is stable, requires no coordination, and distributes uniformly across shards.
/// All nodes in the cluster must agree on ClusterShardCount.
///
/// Ownership knowledge is read from the local IShardOwnershipCache, which is refreshed
/// periodically from Redis by the gateway infrastructure layer.
/// </summary>
public sealed class ClusterShardRouter(
    IShardOwnershipCache ownershipCache,
    IOptions<ClusterShardingOptions> options) : IShardRouter
{
    private readonly ClusterShardingOptions _options = options.Value;

    /// <inheritdoc/>
    public int GetClusterShard(in InfoHashKey infoHash)
    {
        // Use the high 32 bits of the info_hash for shard assignment.
        // Part0 holds the first 8 bytes (big-endian), so we extract the top 4 bytes.
        var top4Bytes = (uint)(infoHash.Part0 >> 32);
        return (int)(top4Bytes % (uint)_options.ClusterShardCount);
    }

    /// <inheritdoc/>
    public string? GetOwnerNodeId(int clusterShardId) => ownershipCache.GetOwnerNodeId(clusterShardId);

    /// <inheritdoc/>
    public bool IsLocallyOwned(int clusterShardId) => ownershipCache.IsLocallyOwned(clusterShardId);

    /// <inheritdoc/>
    public bool IsLocallyOwned(in InfoHashKey infoHash) => ownershipCache.IsLocallyOwned(GetClusterShard(infoHash));

    /// <inheritdoc/>
    public IReadOnlyDictionary<int, string> GetOwnershipSnapshot() => ownershipCache.GetSnapshot();

    /// <inheritdoc/>
    public int LocallyOwnedShardCount => ownershipCache.LocallyOwnedCount;
}

/// <summary>
/// Thread-safe in-memory cache of shard-to-node ownership assignments.
/// Ownership data is snapshotted atomically on each update to avoid partial reads.
/// </summary>
public sealed class LocalShardOwnershipCache : IShardOwnershipCache
{
    // Volatile reference ensures readers always see the latest frozen snapshot.
    private volatile OwnershipSnapshot _snapshot = OwnershipSnapshot.Empty;

    private sealed class OwnershipSnapshot(
        IReadOnlyDictionary<int, string> ownerships,
        string localNodeId)
    {
        public static readonly OwnershipSnapshot Empty =
            new(FrozenDictionary<int, string>.Empty, string.Empty);

        public IReadOnlyDictionary<int, string> Ownerships { get; } = ownerships;
        public string LocalNodeId { get; } = localNodeId;
        public int LocallyOwnedCount { get; } =
            ownerships.Values.Count(id => id.Equals(localNodeId, StringComparison.Ordinal));
    }

    /// <inheritdoc/>
    public string? GetOwnerNodeId(int clusterShardId)
    {
        _snapshot.Ownerships.TryGetValue(clusterShardId, out var nodeId);
        return nodeId;
    }

    /// <inheritdoc/>
    public bool IsLocallyOwned(int clusterShardId)
    {
        var snap = _snapshot;
        return snap.Ownerships.TryGetValue(clusterShardId, out var nodeId)
            && nodeId.Equals(snap.LocalNodeId, StringComparison.Ordinal);
    }

    /// <inheritdoc/>
    public int LocallyOwnedCount => _snapshot.LocallyOwnedCount;

    /// <inheritdoc/>
    public IReadOnlyDictionary<int, string> GetSnapshot() => _snapshot.Ownerships;

    /// <inheritdoc/>
    public void Update(string localNodeId, IReadOnlyDictionary<int, string> ownerships)
    {
        // Freeze the dictionary for lock-free reads after assignment.
        var frozen = ownerships is FrozenDictionary<int, string> fd
            ? fd
            : ownerships.ToFrozenDictionary();

        _snapshot = new OwnershipSnapshot(frozen, localNodeId);
    }
}

public static class ClusterShardRoutingServiceCollectionExtensions
{
    public static IServiceCollection AddClusterShardRouting(this IServiceCollection services)
    {
        services.AddSingleton<LocalShardOwnershipCache>();
        services.AddSingleton<IShardOwnershipCache>(sp => sp.GetRequiredService<LocalShardOwnershipCache>());
        services.AddSingleton<IShardRouter, ClusterShardRouter>();
        return services;
    }
}
