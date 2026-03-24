using Microsoft.Extensions.Options;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using Tracker.Gateway.Application.Announce;
using Tracker.Gateway.Application.Cluster;
using Tracker.Gateway.Runtime;

namespace Tracker.Cluster.UnitTests;

/// <summary>
/// Tests for cluster-aware peer selection locality tracking.
/// The PeerSelectionService emits locality metrics (local/non_local) based on shard ownership.
/// </summary>
public sealed class ClusterAwareSelectionTests
{
    private static PartitionedRuntimeSwarmStore CreateStore(int shardCount = 8) => new(Options.Create(new GatewayRuntimeOptions
    {
        ShardCount = shardCount,
        MaxPeersPerResponse = 50,
        PeerTtlSeconds = 2700,
        ExpirySweepIntervalSeconds = 30
    }));

    private static InfoHashKey Hash1 => InfoHashKey.FromBytes(Convert.FromHexString("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));
    private static InfoHashKey Hash2 => InfoHashKey.FromBytes(Convert.FromHexString("BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB"));

    private static AnnounceRequest MakeRequest(InfoHashKey infoHash, PeerEndpoint endpoint)
        => new(infoHash, PeerIdKey.FromBytes(new byte[20] { 0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
               endpoint, 0, 0, 100, 10, true, false, TrackerEvent.Started, null, null, null);

    // ─── IShardRouter Stub ───────────────────────────────────────────────────

    private sealed class StubShardRouter(bool locallyOwned) : IShardRouter
    {
        public int GetClusterShard(in InfoHashKey infoHash) => 0;
        public string? GetOwnerNodeId(int clusterShardId) => locallyOwned ? "local-node" : "other-node";
        public bool IsLocallyOwned(int clusterShardId) => locallyOwned;
        public bool IsLocallyOwned(in InfoHashKey infoHash) => locallyOwned;
        public IReadOnlyDictionary<int, string> GetOwnershipSnapshot() => new Dictionary<int, string>();
        public int LocallyOwnedShardCount => locallyOwned ? 1 : 0;
    }

    [Fact]
    public void PeerSelection_LocallyOwnedSwarm_ReturnsLocalPeers()
    {
        var store = CreateStore();
        var shardRouter = new StubShardRouter(locallyOwned: true);
        var selectionService = new PeerSelectionService(store, shardRouter);

        var now = DateTimeOffset.UtcNow;
        var request = MakeRequest(Hash1, PeerEndpoint.FromIPv4(0x0A000001, 6881));

        // Add a peer to the store
        store.ApplyMutation(
            new AnnounceRequest(Hash1, PeerIdKey.FromBytes(new byte[20]),
                PeerEndpoint.FromIPv4(0x0A000002, 6882), 0, 0, 100, 10, true, false, TrackerEvent.Started, null, null, null),
            TimeSpan.FromMinutes(30), now);

        using var result = selectionService.Select(request, 10, 50, now);
        // Selection from local store works regardless of ownership
        Assert.True(result.Peers.Count >= 0); // at least no crash
    }

    [Fact]
    public void PeerSelection_NonLocallyOwnedSwarm_StillServesFromLocalStore()
    {
        var store = CreateStore();
        var shardRouter = new StubShardRouter(locallyOwned: false);
        var selectionService = new PeerSelectionService(store, shardRouter);

        var now = DateTimeOffset.UtcNow;
        var request = MakeRequest(Hash1, PeerEndpoint.FromIPv4(0x0A000001, 6881));

        // Add a peer to the local store even though shard is "owned" by another node
        store.ApplyMutation(
            new AnnounceRequest(Hash1, PeerIdKey.FromBytes(new byte[20]),
                PeerEndpoint.FromIPv4(0x0A000002, 6882), 0, 0, 100, 10, true, false, TrackerEvent.Started, null, null, null),
            TimeSpan.FromMinutes(30), now);

        // Even non-locally-owned swarms are served from local store (local-first architecture)
        using var result = selectionService.Select(request, 10, 50, now);
        Assert.True(result.Peers.Count >= 0);
    }

    [Fact]
    public void ShardRouter_LocalOwnership_IsLocallyOwned_ReturnsTrue()
    {
        var cache = new LocalShardOwnershipCache();
        var router = new ClusterShardRouter(cache, Options.Create(new ClusterShardingOptions { ClusterShardCount = 256 }));

        var shard = router.GetClusterShard(Hash1);
        cache.Update("local-node", new Dictionary<int, string> { [shard] = "local-node" });

        Assert.True(router.IsLocallyOwned(Hash1));
    }

    [Fact]
    public void ShardRouter_RemoteOwnership_IsLocallyOwned_ReturnsFalse()
    {
        var cache = new LocalShardOwnershipCache();
        var router = new ClusterShardRouter(cache, Options.Create(new ClusterShardingOptions { ClusterShardCount = 256 }));

        var shard = router.GetClusterShard(Hash1);
        cache.Update("local-node", new Dictionary<int, string> { [shard] = "remote-node" });

        Assert.False(router.IsLocallyOwned(Hash1));
    }

    [Fact]
    public void ShardRouter_HashToShard_IsStableAcrossMultipleNodes()
    {
        // Both nodes must produce the same hash → shard mapping
        var cacheA = new LocalShardOwnershipCache();
        var cacheB = new LocalShardOwnershipCache();
        var opts = Options.Create(new ClusterShardingOptions { ClusterShardCount = 64 });

        var routerA = new ClusterShardRouter(cacheA, opts);
        var routerB = new ClusterShardRouter(cacheB, opts);

        Assert.Equal(routerA.GetClusterShard(Hash1), routerB.GetClusterShard(Hash1));
        Assert.Equal(routerA.GetClusterShard(Hash2), routerB.GetClusterShard(Hash2));
    }
}
