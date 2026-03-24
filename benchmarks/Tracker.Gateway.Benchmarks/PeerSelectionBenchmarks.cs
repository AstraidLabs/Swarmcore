using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Options;
using System.Buffers.Binary;
using Tracker.Gateway.Application.Announce;
using Tracker.Gateway.Application.Cluster;
using Tracker.Gateway.Runtime;

namespace Tracker.Gateway.Benchmarks;

[MemoryDiagnoser]
public class PeerSelectionBenchmarks
{
    private PartitionedRuntimeSwarmStore _store = null!;
    private PeerSelectionService _selectionService = null!;
    private AnnounceRequest _caller;
    private DateTimeOffset _now;

    [Params(100, 1000)]
    public int PeerCount;

    [GlobalSetup]
    public void Setup()
    {
        _store = new PartitionedRuntimeSwarmStore(Options.Create(new GatewayRuntimeOptions
        {
            ShardCount = 64,
            MaxPeersPerResponse = 80,
            PeerTtlSeconds = 2700,
            ExpirySweepIntervalSeconds = 30
        }));

        var mutation = new PeerMutationService(_store);
        _selectionService = new PeerSelectionService(_store, new BenchmarkShardRouter());
        _now = DateTimeOffset.UtcNow;
        var infoHash = InfoHashKey.FromBytes(Convert.FromHexString("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));

        _caller = CreateRequest(infoHash, "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB", 0x7F000001, 5000, 50);
        mutation.Apply(_caller, 1800, _now);

        var peerBytes = new byte[20];
        for (var index = 0; index < PeerCount; index++)
        {
            Array.Clear(peerBytes);
            BinaryPrimitives.WriteInt32BigEndian(peerBytes[^4..], index + 1);
            mutation.Apply(new AnnounceRequest(
                infoHash,
                PeerIdKey.FromBytes(peerBytes),
                PeerEndpoint.FromIPv4(0x7F000001, (ushort)(6000 + index)),
                0,
                0,
                1,
                50,
                true,
                false,
                TrackerEvent.Started,
                null, null, null), 1800, _now);
        }
    }

    [Benchmark]
    public int SelectPeers()
    {
        using var result = _selectionService.Select(_caller, 50, 80, _now);
        return result.Peers.Count + result.Peers6.Count;
    }

    private static AnnounceRequest CreateRequest(InfoHashKey infoHash, string peerIdHex, uint ip, ushort port, int requestedPeers)
        => new(infoHash, PeerIdKey.FromBytes(Convert.FromHexString(peerIdHex)), PeerEndpoint.FromIPv4(ip, port), 0, 0, 1, requestedPeers, true, false, TrackerEvent.Started, null, null, null);

    private sealed class BenchmarkShardRouter : IShardRouter
    {
        public int GetClusterShard(in InfoHashKey infoHash) => 0;
        public string? GetOwnerNodeId(int clusterShardId) => "local-node";
        public bool IsLocallyOwned(int clusterShardId) => true;
        public bool IsLocallyOwned(in InfoHashKey infoHash) => true;
        public IReadOnlyDictionary<int, string> GetOwnershipSnapshot() => new Dictionary<int, string>();
        public int LocallyOwnedShardCount => 1;
    }
}
