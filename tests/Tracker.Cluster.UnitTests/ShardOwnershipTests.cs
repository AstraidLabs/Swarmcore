using System.Collections.Frozen;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using Swarmcore.Contracts.Runtime;
using Tracker.CacheCoordinator.Application;

namespace Tracker.Cluster.UnitTests;

// ─── In-Process Ownership Registry (test double) ─────────────────────────────

/// <summary>
/// In-memory IShardOwnershipRegistry for tests.
/// Implements the same SET NX / compare-and-delete semantics as the Redis implementation.
/// </summary>
internal sealed class InMemoryShardOwnershipRegistry : IShardOwnershipRegistry
{
    private readonly Dictionary<int, ShardOwnershipRecord> _store = [];
    private readonly object _lock = new();

    public Task<ShardOwnershipRecord?> TryClaimAsync(
        int shardId, string nodeId, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_store.TryGetValue(shardId, out var existing))
            {
                return Task.FromResult<ShardOwnershipRecord?>(existing);
            }

            var record = new ShardOwnershipRecord(
                shardId, nodeId, DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.Add(leaseDuration), 1);
            _store[shardId] = record;
            return Task.FromResult<ShardOwnershipRecord?>(record);
        }
    }

    public Task<bool> TryRefreshAsync(
        int shardId, string nodeId, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (!_store.TryGetValue(shardId, out var existing) ||
                !existing.OwnerNodeId.Equals(nodeId, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            _store[shardId] = existing with { LeaseExpiresAtUtc = DateTimeOffset.UtcNow.Add(leaseDuration) };
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryReleaseAsync(int shardId, string nodeId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (!_store.TryGetValue(shardId, out var existing) ||
                !existing.OwnerNodeId.Equals(nodeId, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            _store.Remove(shardId);
            return Task.FromResult(true);
        }
    }

    public Task<ShardOwnershipRecord?> GetOwnershipAsync(int shardId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _store.TryGetValue(shardId, out var record);
            return Task.FromResult(record);
        }
    }

    public Task<IReadOnlyDictionary<int, ShardOwnershipRecord>> GetAllOwnershipsAsync(
        int totalShardCount, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyDictionary<int, ShardOwnershipRecord>>(
                new Dictionary<int, ShardOwnershipRecord>(_store).ToFrozenDictionary());
        }
    }

    /// <summary>
    /// Simulate expiry of a node's leases (remove all its shard records).
    /// Mimics Redis TTL expiry after FailoverTimeoutSeconds.
    /// </summary>
    public void ExpireNode(string nodeId)
    {
        lock (_lock)
        {
            foreach (var shardId in _store.Keys.ToList())
            {
                if (_store[shardId].OwnerNodeId.Equals(nodeId, StringComparison.Ordinal))
                {
                    _store.Remove(shardId);
                }
            }
        }
    }

    public int Count
    {
        get { lock (_lock) { return _store.Count; } }
    }
}

// ─── Ownership Assignment Tests ───────────────────────────────────────────────

public sealed class ShardOwnershipTests
{
    private static ShardOwnershipService CreateService(
        IShardOwnershipRegistry registry,
        int shardCount = 4,
        string nodeId = "node-1")
        => new(registry, Options.Create(new ClusterShardingOptions
        {
            ClusterShardCount = shardCount,
            OwnershipLeaseDurationSeconds = 60,
            OwnershipRefreshIntervalSeconds = 20
        }),
        Options.Create(new TrackerNodeOptions { NodeId = nodeId }),
        NullLogger<ShardOwnershipService>.Instance);

    [Fact]
    public async Task ClaimAvailableShards_EmptyRegistry_ClaimsAll()
    {
        var registry = new InMemoryShardOwnershipRegistry();
        var service = CreateService(registry, shardCount: 4);

        var claimed = await service.ClaimAvailableShardsAsync(CancellationToken.None);

        Assert.Equal(4, claimed);
        Assert.Equal(4, service.OwnedShardIds.Count);
        Assert.Equal(4, registry.Count);
    }

    [Fact]
    public async Task ClaimAvailableShards_AllOwnedByOthers_ClaimsNone()
    {
        var registry = new InMemoryShardOwnershipRegistry();
        var otherService = CreateService(registry, shardCount: 4, nodeId: "other-node");
        await otherService.ClaimAvailableShardsAsync(CancellationToken.None);

        var service = CreateService(registry, shardCount: 4, nodeId: "node-1");
        var claimed = await service.ClaimAvailableShardsAsync(CancellationToken.None);

        Assert.Equal(0, claimed);
        Assert.Empty(service.OwnedShardIds);
    }

    [Fact]
    public async Task ClaimAvailableShards_PartiallyOwned_ClaimsRemainder()
    {
        var registry = new InMemoryShardOwnershipRegistry();

        // Pre-claim shards 0 and 1
        await registry.TryClaimAsync(0, "other-node", TimeSpan.FromSeconds(60), CancellationToken.None);
        await registry.TryClaimAsync(1, "other-node", TimeSpan.FromSeconds(60), CancellationToken.None);

        var service = CreateService(registry, shardCount: 4, nodeId: "node-1");
        var claimed = await service.ClaimAvailableShardsAsync(CancellationToken.None);

        Assert.Equal(2, claimed);
        Assert.Equal([2, 3], service.OwnedShardIds.OrderBy(static x => x).ToArray());
    }

    [Fact]
    public async Task ClaimAvailableShards_ExpiredNode_ClaimsAllShards()
    {
        var registry = new InMemoryShardOwnershipRegistry();
        var deadNode = CreateService(registry, shardCount: 4, nodeId: "dead-node");
        await deadNode.ClaimAvailableShardsAsync(CancellationToken.None);

        // Simulate node failure — all leases expire (Redis TTL)
        registry.ExpireNode("dead-node");
        Assert.Equal(0, registry.Count);

        // New node claims the expired shards
        var newNode = CreateService(registry, shardCount: 4, nodeId: "new-node");
        var claimed = await newNode.ClaimAvailableShardsAsync(CancellationToken.None);

        Assert.Equal(4, claimed);
        Assert.Equal(4, newNode.OwnedShardIds.Count);
    }

    [Fact]
    public async Task RefreshOwnedShards_AllLeasesCurrent_LosesNone()
    {
        var registry = new InMemoryShardOwnershipRegistry();
        var service = CreateService(registry, shardCount: 4);
        await service.ClaimAvailableShardsAsync(CancellationToken.None);

        var lostCount = await service.RefreshOwnedShardsAsync(CancellationToken.None);

        Assert.Equal(0, lostCount);
        Assert.Equal(4, service.OwnedShardIds.Count);
    }

    [Fact]
    public async Task RefreshOwnedShards_SomeExpiredByOtherNode_ReflectsLoss()
    {
        var registry = new InMemoryShardOwnershipRegistry();
        var service = CreateService(registry, shardCount: 4, nodeId: "node-1");
        await service.ClaimAvailableShardsAsync(CancellationToken.None);

        // Simulate shard 2 being seized by another node (e.g., forceful reclaim)
        await registry.TryReleaseAsync(2, "node-1", CancellationToken.None);
        await registry.TryClaimAsync(2, "node-2", TimeSpan.FromSeconds(60), CancellationToken.None);

        var lostCount = await service.RefreshOwnedShardsAsync(CancellationToken.None);

        Assert.Equal(1, lostCount);
        Assert.Equal(3, service.OwnedShardIds.Count);
        Assert.DoesNotContain(2, service.OwnedShardIds);
    }

    [Fact]
    public async Task ReleaseAllShards_ReleasesAllOwnedShards()
    {
        var registry = new InMemoryShardOwnershipRegistry();
        var service = CreateService(registry, shardCount: 4);
        await service.ClaimAvailableShardsAsync(CancellationToken.None);
        Assert.Equal(4, service.OwnedShardIds.Count);

        await service.ReleaseAllShardsAsync(CancellationToken.None);

        Assert.Empty(service.OwnedShardIds);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public async Task ReleaseAllShards_AnotherNodeCanImmediatelyClaim()
    {
        var registry = new InMemoryShardOwnershipRegistry();
        var node1 = CreateService(registry, shardCount: 4, nodeId: "node-1");
        await node1.ClaimAvailableShardsAsync(CancellationToken.None);

        // Graceful drain
        await node1.ReleaseAllShardsAsync(CancellationToken.None);

        var node2 = CreateService(registry, shardCount: 4, nodeId: "node-2");
        var claimed = await node2.ClaimAvailableShardsAsync(CancellationToken.None);

        Assert.Equal(4, claimed);
        Assert.Equal(4, node2.OwnedShardIds.Count);
    }

    [Fact]
    public async Task OwnedShardIds_IsImmutableSnapshot_NotLiveCollection()
    {
        var registry = new InMemoryShardOwnershipRegistry();
        var service = CreateService(registry, shardCount: 4);
        await service.ClaimAvailableShardsAsync(CancellationToken.None);

        var snapshot = service.OwnedShardIds;
        Assert.Equal(4, snapshot.Count);

        // Release shards — snapshot taken before release should still be 4
        await service.ReleaseAllShardsAsync(CancellationToken.None);

        // The interface contract: OwnedShardIds always reflects current state
        Assert.Equal(0, service.OwnedShardIds.Count);
        // But the snapshot we captured before release is independent
        Assert.Equal(4, snapshot.Count);
    }
}

// ─── Shard Ownership Registry Semantics Tests ─────────────────────────────────

public sealed class ShardOwnershipRegistryTests
{
    [Fact]
    public async Task TryClaimAsync_UnownedShard_Succeeds()
    {
        var registry = new InMemoryShardOwnershipRegistry();
        var result = await registry.TryClaimAsync(0, "node-1", TimeSpan.FromSeconds(60), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("node-1", result.OwnerNodeId);
        Assert.Equal(0, result.ShardId);
    }

    [Fact]
    public async Task TryClaimAsync_AlreadyOwnedShard_ReturnsExistingOwner()
    {
        var registry = new InMemoryShardOwnershipRegistry();
        await registry.TryClaimAsync(0, "node-1", TimeSpan.FromSeconds(60), CancellationToken.None);

        // Second claim by a different node should not succeed
        var result = await registry.TryClaimAsync(0, "node-2", TimeSpan.FromSeconds(60), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("node-1", result!.OwnerNodeId); // original owner
    }

    [Fact]
    public async Task TryRefreshAsync_OwnedByThisNode_Succeeds()
    {
        var registry = new InMemoryShardOwnershipRegistry();
        await registry.TryClaimAsync(0, "node-1", TimeSpan.FromSeconds(60), CancellationToken.None);

        var refreshed = await registry.TryRefreshAsync(0, "node-1", TimeSpan.FromSeconds(60), CancellationToken.None);
        Assert.True(refreshed);
    }

    [Fact]
    public async Task TryRefreshAsync_OwnedByOtherNode_Fails()
    {
        var registry = new InMemoryShardOwnershipRegistry();
        await registry.TryClaimAsync(0, "node-1", TimeSpan.FromSeconds(60), CancellationToken.None);

        var refreshed = await registry.TryRefreshAsync(0, "node-2", TimeSpan.FromSeconds(60), CancellationToken.None);
        Assert.False(refreshed);
    }

    [Fact]
    public async Task TryRefreshAsync_Unowned_Fails()
    {
        var registry = new InMemoryShardOwnershipRegistry();
        var refreshed = await registry.TryRefreshAsync(0, "node-1", TimeSpan.FromSeconds(60), CancellationToken.None);
        Assert.False(refreshed);
    }

    [Fact]
    public async Task TryReleaseAsync_ByOwner_Succeeds()
    {
        var registry = new InMemoryShardOwnershipRegistry();
        await registry.TryClaimAsync(0, "node-1", TimeSpan.FromSeconds(60), CancellationToken.None);

        var released = await registry.TryReleaseAsync(0, "node-1", CancellationToken.None);
        Assert.True(released);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public async Task TryReleaseAsync_ByNonOwner_Fails()
    {
        var registry = new InMemoryShardOwnershipRegistry();
        await registry.TryClaimAsync(0, "node-1", TimeSpan.FromSeconds(60), CancellationToken.None);

        var released = await registry.TryReleaseAsync(0, "node-2", CancellationToken.None);
        Assert.False(released);
        Assert.Equal(1, registry.Count); // still owned by node-1
    }

    [Fact]
    public async Task GetAllOwnershipsAsync_ReturnsAllActive()
    {
        var registry = new InMemoryShardOwnershipRegistry();
        await registry.TryClaimAsync(0, "node-a", TimeSpan.FromSeconds(60), CancellationToken.None);
        await registry.TryClaimAsync(1, "node-a", TimeSpan.FromSeconds(60), CancellationToken.None);
        await registry.TryClaimAsync(2, "node-b", TimeSpan.FromSeconds(60), CancellationToken.None);

        var all = await registry.GetAllOwnershipsAsync(4, CancellationToken.None);

        Assert.Equal(3, all.Count);
        Assert.Equal("node-a", all[0].OwnerNodeId);
        Assert.Equal("node-a", all[1].OwnerNodeId);
        Assert.Equal("node-b", all[2].OwnerNodeId);
    }
}

// ─── Failover Scenario Tests ──────────────────────────────────────────────────

public sealed class FailoverTests
{
    [Fact]
    public async Task WhenNodeFails_AnotherNodeClaimsItsShards()
    {
        var registry = new InMemoryShardOwnershipRegistry();

        // Node A claims all shards
        var nodeA = new ShardOwnershipService(
            registry,
            Options.Create(new ClusterShardingOptions { ClusterShardCount = 8, OwnershipLeaseDurationSeconds = 60, OwnershipRefreshIntervalSeconds = 20 }),
            Options.Create(new TrackerNodeOptions { NodeId = "node-a" }),
            NullLogger<ShardOwnershipService>.Instance);
        await nodeA.ClaimAvailableShardsAsync(CancellationToken.None);
        Assert.Equal(8, nodeA.OwnedShardIds.Count);

        // Simulate node A failure — leases expire
        registry.ExpireNode("node-a");

        // Node B detects unowned shards and reclaims them (failover)
        var nodeB = new ShardOwnershipService(
            registry,
            Options.Create(new ClusterShardingOptions { ClusterShardCount = 8, OwnershipLeaseDurationSeconds = 60, OwnershipRefreshIntervalSeconds = 20 }),
            Options.Create(new TrackerNodeOptions { NodeId = "node-b" }),
            NullLogger<ShardOwnershipService>.Instance);
        var claimed = await nodeB.ClaimAvailableShardsAsync(CancellationToken.None);

        Assert.Equal(8, claimed);
        Assert.Equal(8, nodeB.OwnedShardIds.Count);
    }

    [Fact]
    public async Task WhenNodeRejoins_ItCannotReclaimShards_OwnedByAnotherNode()
    {
        var registry = new InMemoryShardOwnershipRegistry();

        // Node A claims shards, then "fails"
        var nodeA = new ShardOwnershipService(
            registry,
            Options.Create(new ClusterShardingOptions { ClusterShardCount = 4, OwnershipLeaseDurationSeconds = 60, OwnershipRefreshIntervalSeconds = 20 }),
            Options.Create(new TrackerNodeOptions { NodeId = "node-a" }),
            NullLogger<ShardOwnershipService>.Instance);
        await nodeA.ClaimAvailableShardsAsync(CancellationToken.None);
        registry.ExpireNode("node-a");

        // Node B takes over during failover
        var nodeB = new ShardOwnershipService(
            registry,
            Options.Create(new ClusterShardingOptions { ClusterShardCount = 4, OwnershipLeaseDurationSeconds = 60, OwnershipRefreshIntervalSeconds = 20 }),
            Options.Create(new TrackerNodeOptions { NodeId = "node-b" }),
            NullLogger<ShardOwnershipService>.Instance);
        await nodeB.ClaimAvailableShardsAsync(CancellationToken.None);
        Assert.Equal(4, nodeB.OwnedShardIds.Count);

        // Node A restarts and tries to claim — should get 0 (all owned by B)
        var nodeARejoined = new ShardOwnershipService(
            registry,
            Options.Create(new ClusterShardingOptions { ClusterShardCount = 4, OwnershipLeaseDurationSeconds = 60, OwnershipRefreshIntervalSeconds = 20 }),
            Options.Create(new TrackerNodeOptions { NodeId = "node-a" }),
            NullLogger<ShardOwnershipService>.Instance);
        var reclaimedByA = await nodeARejoined.ClaimAvailableShardsAsync(CancellationToken.None);

        Assert.Equal(0, reclaimedByA);
        Assert.Empty(nodeARejoined.OwnedShardIds);
    }

    [Fact]
    public async Task GracefulDrain_ReleasesShards_NewNodeCanClaim()
    {
        var registry = new InMemoryShardOwnershipRegistry();

        var node1 = new ShardOwnershipService(
            registry,
            Options.Create(new ClusterShardingOptions { ClusterShardCount = 6, OwnershipLeaseDurationSeconds = 60, OwnershipRefreshIntervalSeconds = 20 }),
            Options.Create(new TrackerNodeOptions { NodeId = "node-1" }),
            NullLogger<ShardOwnershipService>.Instance);
        await node1.ClaimAvailableShardsAsync(CancellationToken.None);
        Assert.Equal(6, node1.OwnedShardIds.Count);

        // Graceful drain: release all shards explicitly
        await node1.ReleaseAllShardsAsync(CancellationToken.None);
        Assert.Empty(node1.OwnedShardIds);
        Assert.Equal(0, registry.Count);

        // New node immediately claims the released shards
        var node2 = new ShardOwnershipService(
            registry,
            Options.Create(new ClusterShardingOptions { ClusterShardCount = 6, OwnershipLeaseDurationSeconds = 60, OwnershipRefreshIntervalSeconds = 20 }),
            Options.Create(new TrackerNodeOptions { NodeId = "node-2" }),
            NullLogger<ShardOwnershipService>.Instance);
        var claimed = await node2.ClaimAvailableShardsAsync(CancellationToken.None);
        Assert.Equal(6, claimed);
    }

    [Fact]
    public async Task TwoNodes_ClaimDisjointShards_WithNoConcurrentRegistry()
    {
        // Verify that when two nodes sequentially claim, they split shards cleanly
        var registry = new InMemoryShardOwnershipRegistry();

        var nodeA = new ShardOwnershipService(
            registry,
            Options.Create(new ClusterShardingOptions { ClusterShardCount = 4, OwnershipLeaseDurationSeconds = 60, OwnershipRefreshIntervalSeconds = 20 }),
            Options.Create(new TrackerNodeOptions { NodeId = "node-a" }),
            NullLogger<ShardOwnershipService>.Instance);

        // Node A claims 0,1 manually
        await registry.TryClaimAsync(0, "node-a", TimeSpan.FromSeconds(60), CancellationToken.None);
        await registry.TryClaimAsync(1, "node-a", TimeSpan.FromSeconds(60), CancellationToken.None);

        // Node B claims what's left
        var nodeB = new ShardOwnershipService(
            registry,
            Options.Create(new ClusterShardingOptions { ClusterShardCount = 4, OwnershipLeaseDurationSeconds = 60, OwnershipRefreshIntervalSeconds = 20 }),
            Options.Create(new TrackerNodeOptions { NodeId = "node-b" }),
            NullLogger<ShardOwnershipService>.Instance);
        var claimed = await nodeB.ClaimAvailableShardsAsync(CancellationToken.None);

        Assert.Equal(2, claimed);
        Assert.Equal([2, 3], nodeB.OwnedShardIds.OrderBy(static x => x).ToArray());
    }
}
