using Microsoft.Extensions.Options;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using Tracker.Gateway.Application.Announce;
using Tracker.Gateway.Application.Cluster;
using Tracker.Gateway.Runtime;

namespace Tracker.Cluster.UnitTests;

public sealed class ShardRoutingTests
{
    private static ClusterShardRouter CreateRouter(int shardCount = 256, string localNodeId = "node-1")
    {
        var cache = new LocalShardOwnershipCache();
        cache.Update(localNodeId, new Dictionary<int, string>());
        return new ClusterShardRouter(cache, Options.Create(new ClusterShardingOptions
        {
            ClusterShardCount = shardCount
        }));
    }

    private static InfoHashKey MakeHash(byte firstByte) =>
        InfoHashKey.FromBytes(new byte[20] { firstByte, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });

    // ─── Determinism ─────────────────────────────────────────────────────────

    [Fact]
    public void GetClusterShard_SameHash_ReturnsSameShardEveryTime()
    {
        var router = CreateRouter();
        var hash = MakeHash(0x42);

        var shard1 = router.GetClusterShard(hash);
        var shard2 = router.GetClusterShard(hash);
        var shard3 = router.GetClusterShard(hash);

        Assert.Equal(shard1, shard2);
        Assert.Equal(shard1, shard3);
    }

    [Fact]
    public void GetClusterShard_ReturnsValueWithinBounds()
    {
        var router = CreateRouter(shardCount: 16);
        for (var i = 0; i < 256; i++)
        {
            var hash = MakeHash((byte)i);
            var shard = router.GetClusterShard(hash);
            Assert.InRange(shard, 0, 15);
        }
    }

    [Fact]
    public void GetClusterShard_DifferentHashes_DistributeAcrossShards()
    {
        var router = CreateRouter(shardCount: 8);
        var seen = new HashSet<int>();

        for (var i = 0; i < 256; i++)
        {
            var hash = MakeHash((byte)i);
            seen.Add(router.GetClusterShard(hash));
        }

        // With 256 distinct inputs and 8 shards, all shards should be covered
        Assert.Equal(8, seen.Count);
    }

    [Fact]
    public void GetClusterShard_AllNodesMustAgreeOnMapping()
    {
        // Two routers with same config must produce identical shard assignments
        var router1 = CreateRouter(shardCount: 64, localNodeId: "node-a");
        var router2 = CreateRouter(shardCount: 64, localNodeId: "node-b");

        for (var i = 0; i < 256; i++)
        {
            var hash = MakeHash((byte)i);
            Assert.Equal(router1.GetClusterShard(hash), router2.GetClusterShard(hash));
        }
    }

    // ─── Ownership Cache ──────────────────────────────────────────────────────

    [Fact]
    public void IsLocallyOwned_WhenCacheEmpty_ReturnsFalse()
    {
        var router = CreateRouter();
        var hash = MakeHash(0x10);
        Assert.False(router.IsLocallyOwned(hash));
    }

    [Fact]
    public void IsLocallyOwned_WhenShardOwnedByLocalNode_ReturnsTrue()
    {
        var cache = new LocalShardOwnershipCache();
        var router = new ClusterShardRouter(cache, Options.Create(new ClusterShardingOptions { ClusterShardCount = 256 }));

        var hash = MakeHash(0x10);
        var shard = router.GetClusterShard(hash);
        cache.Update("local-node", new Dictionary<int, string> { [shard] = "local-node" });

        Assert.True(router.IsLocallyOwned(hash));
        Assert.True(router.IsLocallyOwned(shard));
    }

    [Fact]
    public void IsLocallyOwned_WhenShardOwnedByOtherNode_ReturnsFalse()
    {
        var cache = new LocalShardOwnershipCache();
        var router = new ClusterShardRouter(cache, Options.Create(new ClusterShardingOptions { ClusterShardCount = 256 }));

        var hash = MakeHash(0x20);
        var shard = router.GetClusterShard(hash);
        cache.Update("local-node", new Dictionary<int, string> { [shard] = "other-node" });

        Assert.False(router.IsLocallyOwned(hash));
    }

    [Fact]
    public void GetOwnerNodeId_ReturnsCorrectOwner()
    {
        var cache = new LocalShardOwnershipCache();
        var router = new ClusterShardRouter(cache, Options.Create(new ClusterShardingOptions { ClusterShardCount = 256 }));

        cache.Update("local-node", new Dictionary<int, string> { [42] = "remote-node" });

        Assert.Equal("remote-node", router.GetOwnerNodeId(42));
        Assert.Null(router.GetOwnerNodeId(99));
    }

    [Fact]
    public void LocallyOwnedShardCount_ReflectsCurrentOwnership()
    {
        var cache = new LocalShardOwnershipCache();
        var router = new ClusterShardRouter(cache, Options.Create(new ClusterShardingOptions { ClusterShardCount = 256 }));

        cache.Update("local-node", new Dictionary<int, string>
        {
            [0] = "local-node",
            [1] = "local-node",
            [2] = "other-node",
            [3] = "local-node"
        });

        Assert.Equal(3, router.LocallyOwnedShardCount);
    }

    [Fact]
    public void GetOwnershipSnapshot_ReturnsPointInTimeSnapshot()
    {
        var cache = new LocalShardOwnershipCache();
        var router = new ClusterShardRouter(cache, Options.Create(new ClusterShardingOptions { ClusterShardCount = 256 }));

        var initial = new Dictionary<int, string> { [5] = "node-a" };
        cache.Update("local-node", initial);

        var snapshot = router.GetOwnershipSnapshot();
        Assert.Single(snapshot);
        Assert.Equal("node-a", snapshot[5]);

        // Update cache — snapshot should not change (it's a separate object)
        cache.Update("local-node", new Dictionary<int, string> { [5] = "node-b", [6] = "node-c" });
        Assert.Single(snapshot); // old snapshot unchanged
    }
}

// ─── Local Shard Ownership Cache Tests ───────────────────────────────────────

public sealed class LocalShardOwnershipCacheTests
{
    [Fact]
    public void Update_AtomicallyReplacesSnapshot()
    {
        var cache = new LocalShardOwnershipCache();

        cache.Update("node-1", new Dictionary<int, string> { [0] = "node-1" });
        Assert.Equal(1, cache.LocallyOwnedCount);

        cache.Update("node-1", new Dictionary<int, string> { [0] = "node-2" }); // lost shard 0
        Assert.Equal(0, cache.LocallyOwnedCount);
    }

    [Fact]
    public void Update_EmptyOwnerships_ResetsCache()
    {
        var cache = new LocalShardOwnershipCache();
        cache.Update("node-1", new Dictionary<int, string> { [1] = "node-1", [2] = "node-1" });
        cache.Update("node-1", new Dictionary<int, string>());

        Assert.Equal(0, cache.LocallyOwnedCount);
        Assert.Null(cache.GetOwnerNodeId(1));
        Assert.False(cache.IsLocallyOwned(1));
    }

    [Fact]
    public void GetOwnerNodeId_UnknownShard_ReturnsNull()
    {
        var cache = new LocalShardOwnershipCache();
        Assert.Null(cache.GetOwnerNodeId(999));
    }

    [Fact]
    public void IsLocallyOwned_CaseSensitiveComparison()
    {
        var cache = new LocalShardOwnershipCache();
        cache.Update("NODE-1", new Dictionary<int, string> { [0] = "NODE-1" });
        Assert.True(cache.IsLocallyOwned(0));

        // Different case of node ID should not match
        cache.Update("node-1", new Dictionary<int, string> { [0] = "NODE-1" });
        Assert.False(cache.IsLocallyOwned(0)); // local is "node-1", owner is "NODE-1"
    }

    [Fact]
    public void LocallyOwnedCount_OnlyCountsThisNode()
    {
        var cache = new LocalShardOwnershipCache();
        cache.Update("local", new Dictionary<int, string>
        {
            [0] = "local",
            [1] = "other",
            [2] = "local",
            [3] = "yet-another",
            [4] = "local",
        });

        Assert.Equal(3, cache.LocallyOwnedCount);
    }
}
