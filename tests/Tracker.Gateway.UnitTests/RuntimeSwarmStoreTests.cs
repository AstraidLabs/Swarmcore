using Microsoft.Extensions.Options;
using Tracker.Gateway.Application.Announce;
using Tracker.Gateway.Runtime;

namespace Tracker.Gateway.UnitTests;

public sealed class RuntimeSwarmStoreTests
{
    private static PartitionedRuntimeSwarmStore CreateStore(int shardCount = 8) => new(Options.Create(new GatewayRuntimeOptions
    {
        ShardCount = shardCount,
        MaxPeersPerResponse = 50,
        PeerTtlSeconds = 2700,
        ExpirySweepIntervalSeconds = 30
    }));

    private static AnnounceRequest MakeRequest(
        InfoHashKey infoHash,
        string peerIdHex,
        PeerEndpoint endpoint,
        TrackerEvent evt = TrackerEvent.Started,
        long left = 100,
        int requestedPeers = 50)
    {
        return new AnnounceRequest(
            infoHash, PeerIdKey.FromBytes(Convert.FromHexString(peerIdHex)),
            endpoint, 0, 0, left, requestedPeers, true, false, evt, null, null, null);
    }

    private static InfoHashKey Hash1 => InfoHashKey.FromBytes(Convert.FromHexString("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));

    [Fact]
    public void StoppedEvent_RemovesPeerFromCounts()
    {
        var store = CreateStore();
        var now = DateTimeOffset.UtcNow;
        var req = MakeRequest(Hash1, "1111111111111111111111111111111111111111", PeerEndpoint.FromIPv4(0x0A000001, 6881));

        store.ApplyMutation(req, TimeSpan.FromMinutes(30), now);
        var counts = store.GetCounts(Hash1, now);
        Assert.Equal(0, counts.SeederCount);
        Assert.Equal(1, counts.LeecherCount);

        var stopped = MakeRequest(Hash1, "1111111111111111111111111111111111111111",
            PeerEndpoint.FromIPv4(0x0A000001, 6881), TrackerEvent.Stopped, left: 100);
        store.ApplyMutation(stopped, TimeSpan.FromMinutes(30), now);

        counts = store.GetCounts(Hash1, now);
        Assert.Equal(0, counts.SeederCount);
        Assert.Equal(0, counts.LeecherCount);
    }

    [Fact]
    public void StoppedEvent_ForUnknownPeer_DoesNotCrash()
    {
        var store = CreateStore();
        var now = DateTimeOffset.UtcNow;
        var stopped = MakeRequest(Hash1, "2222222222222222222222222222222222222222",
            PeerEndpoint.FromIPv4(0x0A000001, 6882), TrackerEvent.Stopped);

        var counts = store.ApplyMutation(stopped, TimeSpan.FromMinutes(30), now);
        Assert.Equal(0, counts.SeederCount);
        Assert.Equal(0, counts.LeecherCount);
    }

    [Fact]
    public void StoppedEvent_DoesNotReturnPeersToSelect()
    {
        var store = CreateStore();
        var now = DateTimeOffset.UtcNow;
        var peer1 = MakeRequest(Hash1, "3333333333333333333333333333333333333333", PeerEndpoint.FromIPv4(0x0A000001, 6881));
        var peer2 = MakeRequest(Hash1, "4444444444444444444444444444444444444444", PeerEndpoint.FromIPv4(0x0A000002, 6882));
        var stopped = MakeRequest(Hash1, "3333333333333333333333333333333333333333",
            PeerEndpoint.FromIPv4(0x0A000001, 6881), TrackerEvent.Stopped);

        store.ApplyMutation(peer1, TimeSpan.FromMinutes(30), now);
        store.ApplyMutation(peer2, TimeSpan.FromMinutes(30), now);

        using var selection1 = store.SelectPeers(peer2, 10, now);
        Assert.Equal(1, selection1.Peers.Count);

        store.ApplyMutation(stopped, TimeSpan.FromMinutes(30), now);

        using var selection2 = store.SelectPeers(peer2, 10, now);
        Assert.Equal(0, selection2.Peers.Count);
    }

    [Fact]
    public void CompletedEvent_TransitionsLeecherToSeeder()
    {
        var store = CreateStore();
        var now = DateTimeOffset.UtcNow;

        var started = MakeRequest(Hash1, "5555555555555555555555555555555555555555",
            PeerEndpoint.FromIPv4(0x0A000001, 6881), TrackerEvent.Started, left: 100);
        store.ApplyMutation(started, TimeSpan.FromMinutes(30), now);
        var counts = store.GetCounts(Hash1, now);
        Assert.Equal(0, counts.SeederCount);
        Assert.Equal(1, counts.LeecherCount);

        var completed = MakeRequest(Hash1, "5555555555555555555555555555555555555555",
            PeerEndpoint.FromIPv4(0x0A000001, 6881), TrackerEvent.Completed, left: 0);
        store.ApplyMutation(completed, TimeSpan.FromMinutes(30), now);

        counts = store.GetCounts(Hash1, now);
        Assert.Equal(1, counts.SeederCount);
        Assert.Equal(0, counts.LeecherCount);
        Assert.Equal(1, counts.DownloadedCount);
    }

    [Fact]
    public void MultipleCompletedEvents_CountedOnce()
    {
        var store = CreateStore();
        var now = DateTimeOffset.UtcNow;

        var started = MakeRequest(Hash1, "6666666666666666666666666666666666666666",
            PeerEndpoint.FromIPv4(0x0A000001, 6881), TrackerEvent.Started, left: 50);
        store.ApplyMutation(started, TimeSpan.FromMinutes(30), now);

        var completed = MakeRequest(Hash1, "6666666666666666666666666666666666666666",
            PeerEndpoint.FromIPv4(0x0A000001, 6881), TrackerEvent.Completed, left: 0);
        store.ApplyMutation(completed, TimeSpan.FromMinutes(30), now);
        store.ApplyMutation(completed, TimeSpan.FromMinutes(30), now.AddSeconds(5));
        store.ApplyMutation(completed, TimeSpan.FromMinutes(30), now.AddSeconds(10));

        var counts = store.GetCounts(Hash1, now.AddSeconds(10));
        Assert.Equal(1, counts.DownloadedCount);
    }

    [Fact]
    public void IPv4AndIPv6_InSameSwarm_CountedTogether()
    {
        var store = CreateStore();
        var now = DateTimeOffset.UtcNow;

        var v4Peer = MakeRequest(Hash1, "7777777777777777777777777777777777777777",
            PeerEndpoint.FromIPv4(0x0A000001, 6881), left: 100);
        var v6Peer = MakeRequest(Hash1, "8888888888888888888888888888888888888888",
            PeerEndpoint.FromIPv6(Convert.FromHexString("20010DB8000000000000000000000001"), 6882), left: 0);

        store.ApplyMutation(v4Peer, TimeSpan.FromMinutes(30), now);
        store.ApplyMutation(v6Peer, TimeSpan.FromMinutes(30), now);

        var counts = store.GetCounts(Hash1, now);
        Assert.Equal(1, counts.SeederCount);
        Assert.Equal(1, counts.LeecherCount);
    }

    [Fact]
    public void DualFamilySelection_ReturnsBothFamilies()
    {
        var store = CreateStore();
        var now = DateTimeOffset.UtcNow;

        var v4Peer = MakeRequest(Hash1, "9999999999999999999999999999999999999999",
            PeerEndpoint.FromIPv4(0x0A000001, 6881));
        var v6Peer = MakeRequest(Hash1, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABB",
            PeerEndpoint.FromIPv6(Convert.FromHexString("20010DB8000000000000000000000001"), 6882));
        var caller = MakeRequest(Hash1, "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC",
            PeerEndpoint.FromIPv4(0x0A000002, 7000));

        store.ApplyMutation(v4Peer, TimeSpan.FromMinutes(30), now);
        store.ApplyMutation(v6Peer, TimeSpan.FromMinutes(30), now);
        store.ApplyMutation(caller, TimeSpan.FromMinutes(30), now);

        using var selection = store.SelectPeers(caller, 10, now);
        Assert.Equal(1, selection.Peers.Count);
        Assert.Equal(1, selection.Peers6.Count);
    }

    [Fact]
    public void SweepExpired_RemovesEmptySwarms()
    {
        var store = CreateStore();
        var now = DateTimeOffset.UtcNow;

        var req = MakeRequest(Hash1, "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD",
            PeerEndpoint.FromIPv4(0x0A000001, 6881));
        store.ApplyMutation(req, TimeSpan.FromSeconds(1), now);

        Assert.Equal(1, store.GetTotalSwarmCount());

        store.SweepExpired(now.AddSeconds(5));

        Assert.Equal(0, store.GetTotalSwarmCount());
        Assert.Equal(0, store.GetTotalPeerCount());
    }

    [Fact]
    public void ConcurrentMutations_DoNotCorruptCounts()
    {
        var store = CreateStore(shardCount: 4);
        var now = DateTimeOffset.UtcNow;
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        Parallel.For(0, 100, i =>
        {
            try
            {
                var peerIdHex = $"{i:X40}";
                if (peerIdHex.Length > 40) peerIdHex = peerIdHex[..40];
                else peerIdHex = peerIdHex.PadLeft(40, '0');

                var req = MakeRequest(Hash1, peerIdHex,
                    PeerEndpoint.FromIPv4((uint)(0x0A000000 + i), (ushort)(6000 + i)));
                store.ApplyMutation(req, TimeSpan.FromMinutes(30), now);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);

        var counts = store.GetCounts(Hash1, now);
        Assert.Equal(100, counts.SeederCount + counts.LeecherCount);
    }

    [Fact]
    public void GetCounts_UnknownSwarm_ReturnsZero()
    {
        var store = CreateStore();
        var unknown = InfoHashKey.FromBytes(Convert.FromHexString("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"));

        var counts = store.GetCounts(unknown, DateTimeOffset.UtcNow);

        Assert.Equal(0, counts.SeederCount);
        Assert.Equal(0, counts.LeecherCount);
        Assert.Equal(0, counts.DownloadedCount);
    }

    [Fact]
    public void SelectPeers_WithZeroPeerCount_ReturnsDefault()
    {
        var store = CreateStore();
        var now = DateTimeOffset.UtcNow;
        var req = MakeRequest(Hash1, "EEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEE",
            PeerEndpoint.FromIPv4(0x0A000001, 6881), requestedPeers: 0);

        store.ApplyMutation(req, TimeSpan.FromMinutes(30), now);

        using var selection = store.SelectPeers(req, 0, now);
        Assert.Equal(0, selection.Peers.Count);
        Assert.Equal(0, selection.Peers6.Count);
    }
}
