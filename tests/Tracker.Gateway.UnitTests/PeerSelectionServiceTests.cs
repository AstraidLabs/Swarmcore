using Microsoft.Extensions.Options;
using Tracker.Gateway.Application.Announce;
using Tracker.Gateway.Application.Cluster;
using Tracker.Gateway.Runtime;

namespace Tracker.Gateway.UnitTests;

public sealed class PeerSelectionServiceTests
{
    [Fact]
    public void Selection_DoesNotReturnCallerPeer()
    {
        var runtimeStore = new PartitionedRuntimeSwarmStore(Options.Create(new GatewayRuntimeOptions
        {
            ShardCount = 8,
            MaxPeersPerResponse = 50,
            PeerTtlSeconds = 2700,
            ExpirySweepIntervalSeconds = 30
        }));

        var mutationService = new PeerMutationService(runtimeStore);
        var selectionService = new PeerSelectionService(runtimeStore, new StubShardRouter());
        var now = DateTimeOffset.UtcNow;
        var infoHash = InfoHashKey.FromBytes(Convert.FromHexString("0102030405060708090A0B0C0D0E0F1011121314"));

        var caller = CreateRequest(infoHash, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", PeerEndpoint.FromIPv4(0x7F000001, 5001), 10);
        var peer2 = CreateRequest(infoHash, "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB", PeerEndpoint.FromIPv4(0x7F000001, 5002), 10);
        var peer3 = CreateRequest(infoHash, "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC", PeerEndpoint.FromIPv4(0x7F000001, 5003), 10);

        mutationService.Apply(caller, 1800, now);
        mutationService.Apply(peer2, 1800, now);
        mutationService.Apply(peer3, 1800, now);

        using var result = selectionService.Select(caller, 10, 50, now);

        Assert.Equal(2, result.Peers.Count);
        Assert.DoesNotContain(result.Peers.AsSpan().ToArray(), peer => peer.Port == 5001);
    }

    [Fact]
    public void ExpiredPeer_IsRemovedFromCounts()
    {
        var runtimeStore = new PartitionedRuntimeSwarmStore(Options.Create(new GatewayRuntimeOptions
        {
            ShardCount = 8,
            MaxPeersPerResponse = 50,
            PeerTtlSeconds = 60,
            ExpirySweepIntervalSeconds = 1
        }));

        var request = CreateRequest(
            InfoHashKey.FromBytes(Convert.FromHexString("1111111111111111111111111111111111111111")),
            "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD",
            PeerEndpoint.FromIPv4(0x7F000001, 5004),
            10);

        runtimeStore.ApplyMutation(request, TimeSpan.FromSeconds(1), DateTimeOffset.UtcNow);
        runtimeStore.SweepExpired(DateTimeOffset.UtcNow.AddSeconds(2));

        var counts = runtimeStore.GetCounts(request.InfoHash, DateTimeOffset.UtcNow.AddSeconds(2));
        Assert.Equal(0, counts.SeederCount);
        Assert.Equal(0, counts.LeecherCount);
        Assert.Equal(0, counts.DownloadedCount);
    }

    [Fact]
    public void CompletedEvent_IsCountedOnce()
    {
        var runtimeStore = new PartitionedRuntimeSwarmStore(Options.Create(new GatewayRuntimeOptions
        {
            ShardCount = 8,
            MaxPeersPerResponse = 50,
            PeerTtlSeconds = 2700,
            ExpirySweepIntervalSeconds = 30
        }));

        var now = DateTimeOffset.UtcNow;
        var infoHash = InfoHashKey.FromBytes(Convert.FromHexString("1111111111111111111111111111111111111111"));
        var started = CreateRequest(infoHash, "EEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEE", PeerEndpoint.FromIPv4(0x7F000001, 5005), 10, TrackerEvent.Started, left: 100);
        var completed = CreateRequest(infoHash, "EEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEE", PeerEndpoint.FromIPv4(0x7F000001, 5005), 10, TrackerEvent.Completed, left: 0);

        runtimeStore.ApplyMutation(started, TimeSpan.FromMinutes(30), now);
        runtimeStore.ApplyMutation(completed, TimeSpan.FromMinutes(30), now);
        runtimeStore.ApplyMutation(completed, TimeSpan.FromMinutes(30), now.AddSeconds(5));

        var counts = runtimeStore.GetCounts(infoHash, now.AddSeconds(5));
        Assert.Equal(1, counts.SeederCount);
        Assert.Equal(0, counts.LeecherCount);
        Assert.Equal(1, counts.DownloadedCount);
    }

    [Fact]
    public void Selection_ForIpv6Requester_ReturnsBothFamilies()
    {
        var runtimeStore = new PartitionedRuntimeSwarmStore(Options.Create(new GatewayRuntimeOptions
        {
            ShardCount = 8,
            MaxPeersPerResponse = 50,
            PeerTtlSeconds = 2700,
            ExpirySweepIntervalSeconds = 30
        }));

        var mutationService = new PeerMutationService(runtimeStore);
        var selectionService = new PeerSelectionService(runtimeStore, new StubShardRouter());
        var now = DateTimeOffset.UtcNow;
        var infoHash = InfoHashKey.FromBytes(Convert.FromHexString("2222222222222222222222222222222222222222"));
        var caller = CreateRequest(infoHash, "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", PeerEndpoint.FromIPv6(Convert.FromHexString("20010DB8000000000000000000000001"), 5006), 10);
        var peer = CreateRequest(infoHash, "9999999999999999999999999999999999999999", PeerEndpoint.FromIPv6(Convert.FromHexString("20010DB8000000000000000000000002"), 5007), 10);
        var ipv4Peer = CreateRequest(infoHash, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", PeerEndpoint.FromIPv4(0x7F000001, 5008), 10);

        mutationService.Apply(caller, 1800, now);
        mutationService.Apply(peer, 1800, now);
        mutationService.Apply(ipv4Peer, 1800, now);

        using var result = selectionService.Select(caller, 10, 50, now);

        Assert.Equal(1, result.Peers.Count);
        Assert.Equal(1, result.Peers6.Count);
    }

    private static AnnounceRequest CreateRequest(
        InfoHashKey infoHash,
        string peerIdHex,
        PeerEndpoint endpoint,
        int requestedPeers,
        TrackerEvent trackerEvent = TrackerEvent.Started,
        long left = 100)
    {
        return new AnnounceRequest(
            infoHash,
            PeerIdKey.FromBytes(Convert.FromHexString(peerIdHex)),
            endpoint,
            0,
            0,
            left,
            requestedPeers,
            true,
            trackerEvent,
            null);
    }

    private sealed class StubShardRouter : IShardRouter
    {
        public int GetClusterShard(in InfoHashKey infoHash) => 0;
        public string? GetOwnerNodeId(int clusterShardId) => "local-node";
        public bool IsLocallyOwned(int clusterShardId) => true;
        public bool IsLocallyOwned(in InfoHashKey infoHash) => true;
        public IReadOnlyDictionary<int, string> GetOwnershipSnapshot() => new Dictionary<int, string>();
        public int LocallyOwnedShardCount => 1;
    }
}
