using System.Buffers.Binary;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using Microsoft.Extensions.Options;
using Tracker.Gateway.Application.Announce;
using Tracker.Gateway.Runtime;

namespace Tracker.Gateway.Benchmarks;

[MemoryDiagnoser]
[ThreadingDiagnoser]
public class SwarmUpdateContentionBenchmarks
{
    private PartitionedRuntimeSwarmStore _store = null!;
    private PeerMutationService _mutationService = null!;
    private AnnounceRequest[] _singleSwarmRequests = null!;
    private AnnounceRequest[] _distributedSwarmRequests = null!;
    private DateTimeOffset _now;

    [Params(256, 1024)]
    public int OperationCount;

    [Params(4, 16)]
    public int Parallelism;

    [GlobalSetup]
    public void Setup()
    {
        _singleSwarmRequests = CreateRequests(distributeAcrossSwarms: false, OperationCount);
        _distributedSwarmRequests = CreateRequests(distributeAcrossSwarms: true, OperationCount);
        ResetStore();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        ResetStore();
    }

    [Benchmark]
    public int MutateSingleSwarmHotspot()
    {
        return RunMutations(_singleSwarmRequests);
    }

    [Benchmark]
    public int MutateAcrossMultipleSwarms()
    {
        return RunMutations(_distributedSwarmRequests);
    }

    private void ResetStore()
    {
        _store = new PartitionedRuntimeSwarmStore(Options.Create(new GatewayRuntimeOptions
        {
            ShardCount = 64,
            MaxPeersPerResponse = 80,
            PeerTtlSeconds = 2700,
            ExpirySweepIntervalSeconds = 30
        }));

        _mutationService = new PeerMutationService(_store);
        _now = DateTimeOffset.UtcNow;
    }

    private int RunMutations(AnnounceRequest[] requests)
    {
        var counts = new int[Parallelism];

        Parallel.For(
            0,
            requests.Length,
            new ParallelOptions { MaxDegreeOfParallelism = Parallelism },
            () => 0,
            (index, _, localCount) =>
            {
                var result = _mutationService.Apply(requests[index], 1800, _now);
                return localCount + result.SeederCount + result.LeecherCount;
            },
            localCount =>
            {
                var slot = Thread.GetCurrentProcessorId() % counts.Length;
                Interlocked.Add(ref counts[slot], localCount);
            });

        return counts.Sum();
    }

    private static AnnounceRequest[] CreateRequests(bool distributeAcrossSwarms, int operationCount)
    {
        var requests = new AnnounceRequest[operationCount];
        var peerBytes = new byte[20];
        var infoHashBytes = new byte[20];

        for (var index = 0; index < operationCount; index++)
        {
            Array.Clear(peerBytes);
            BinaryPrimitives.WriteInt32BigEndian(peerBytes[^4..], index + 1);

            Array.Clear(infoHashBytes);
            BinaryPrimitives.WriteInt32BigEndian(infoHashBytes[^4..], distributeAcrossSwarms ? (index % 128) + 1 : 1);

            requests[index] = new AnnounceRequest(
                InfoHashKey.FromBytes(infoHashBytes),
                PeerIdKey.FromBytes(peerBytes),
                PeerEndpoint.FromIPv4(0x7F000001, (ushort)(40000 + (index % 20000))),
                0,
                0,
                index % 5 == 0 ? 0 : 1,
                50,
                true,
                TrackerEvent.Started,
                null);
        }

        return requests;
    }
}
