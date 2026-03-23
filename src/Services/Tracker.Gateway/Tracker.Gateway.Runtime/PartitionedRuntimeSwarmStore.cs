using System.Buffers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tracker.Gateway.Application.Announce;

namespace Tracker.Gateway.Runtime;

internal sealed class SwarmState
{
    public readonly object SyncRoot = new();
    public readonly Dictionary<PeerIdKey, int> IndexByPeerId = [];
    public readonly List<PeerState> Peers = [];
    public int SeederCount;
    public int LeecherCount;
    public int DownloadedCount;
    public long NextSweepUnixSeconds;
    public int SelectionCursorV4;
    public int SelectionCursorV6;
}

internal sealed class PeerState
{
    public PeerIdKey PeerId { get; init; }
    public PeerEndpoint Endpoint { get; set; }
    public long Uploaded { get; set; }
    public long Downloaded { get; set; }
    public long Left { get; set; }
    public long ExpiresAtUnixSeconds { get; set; }
    public bool IsSeeder { get; set; }
    public bool CompletionRecorded { get; set; }
}

internal sealed class SwarmShard
{
    public object SyncRoot { get; } = new();
    public Dictionary<InfoHashKey, SwarmState> Swarms { get; } = [];
}

public sealed class PartitionedRuntimeSwarmStore : IRuntimeSwarmStore
{
    private readonly SwarmShard[] _shards;
    private readonly GatewayRuntimeOptions _options;

    public PartitionedRuntimeSwarmStore(IOptions<GatewayRuntimeOptions> options)
    {
        _options = options.Value;
        var shardCount = Math.Max(4, _options.ShardCount);
        _shards = Enumerable.Range(0, shardCount).Select(static _ => new SwarmShard()).ToArray();
    }

    public SwarmCounts ApplyMutation(in AnnounceRequest request, TimeSpan peerTtl, DateTimeOffset now)
    {
        var swarm = GetOrCreateSwarm(request.InfoHash);
        var nowSeconds = now.ToUnixTimeSeconds();
        var expiresAt = nowSeconds + (long)peerTtl.TotalSeconds;

        lock (swarm.SyncRoot)
        {
            if (nowSeconds >= swarm.NextSweepUnixSeconds)
            {
                PruneExpiredUnlocked(swarm, nowSeconds);
                swarm.NextSweepUnixSeconds = nowSeconds + _options.ExpirySweepIntervalSeconds;
            }

            if (request.Event == TrackerEvent.Stopped)
            {
                RemovePeerUnlocked(swarm, request.PeerId);
                return new SwarmCounts(swarm.SeederCount, swarm.LeecherCount, swarm.DownloadedCount);
            }

            var isSeeder = request.IsSeeder;
            if (swarm.IndexByPeerId.TryGetValue(request.PeerId, out var existingIndex))
            {
                var existing = swarm.Peers[existingIndex];
                if (ShouldRecordCompleted(existing, request))
                {
                    swarm.DownloadedCount++;
                    existing.CompletionRecorded = true;
                }

                if (existing.IsSeeder != isSeeder)
                {
                    if (existing.IsSeeder)
                    {
                        swarm.SeederCount--;
                        swarm.LeecherCount++;
                    }
                    else
                    {
                        swarm.SeederCount++;
                        swarm.LeecherCount--;
                    }
                }

                existing.Endpoint = request.Endpoint;
                existing.Uploaded = request.Uploaded;
                existing.Downloaded = request.Downloaded;
                existing.Left = request.Left;
                existing.ExpiresAtUnixSeconds = expiresAt;
                existing.IsSeeder = isSeeder;
            }
            else
            {
                swarm.IndexByPeerId.Add(request.PeerId, swarm.Peers.Count);
                var completionRecorded = request.Event == TrackerEvent.Completed && request.IsSeeder;
                if (completionRecorded)
                {
                    swarm.DownloadedCount++;
                }

                swarm.Peers.Add(new PeerState
                {
                    PeerId = request.PeerId,
                    Endpoint = request.Endpoint,
                    Uploaded = request.Uploaded,
                    Downloaded = request.Downloaded,
                    Left = request.Left,
                    ExpiresAtUnixSeconds = expiresAt,
                    IsSeeder = isSeeder,
                    CompletionRecorded = completionRecorded
                });

                if (isSeeder)
                {
                    swarm.SeederCount++;
                }
                else
                {
                    swarm.LeecherCount++;
                }
            }

            return new SwarmCounts(swarm.SeederCount, swarm.LeecherCount, swarm.DownloadedCount);
        }
    }

    public AnnouncePeerSelection SelectPeers(in AnnounceRequest request, int peerCount, DateTimeOffset now)
    {
        if (peerCount <= 0)
        {
            return default;
        }

        var swarm = GetExistingSwarm(request.InfoHash);
        if (swarm is null)
        {
            return default;
        }

        var nowSeconds = now.ToUnixTimeSeconds();
        var rentedV4 = ArrayPool<SelectedPeer>.Shared.Rent(peerCount);
        var rentedV6 = ArrayPool<SelectedPeer>.Shared.Rent(peerCount);
        var selectedV4 = 0;
        var selectedV6 = 0;

        lock (swarm.SyncRoot)
        {
            if (nowSeconds >= swarm.NextSweepUnixSeconds)
            {
                PruneExpiredUnlocked(swarm, nowSeconds);
                swarm.NextSweepUnixSeconds = nowSeconds + _options.ExpirySweepIntervalSeconds;
            }

            if (swarm.Peers.Count == 0)
            {
                ArrayPool<SelectedPeer>.Shared.Return(rentedV4, clearArray: false);
                ArrayPool<SelectedPeer>.Shared.Return(rentedV6, clearArray: false);
                return default;
            }

            var startV4 = (++swarm.SelectionCursorV4) & 0x7FFFFFFF;
            var startV6 = (++swarm.SelectionCursorV6) & 0x7FFFFFFF;
            var peerCountInSwarm = swarm.Peers.Count;

            for (var offset = 0; offset < peerCountInSwarm && (selectedV4 < peerCount || selectedV6 < peerCount); offset++)
            {
                var peer = swarm.Peers[(startV4 + offset) % peerCountInSwarm];

                if (peer.PeerId == request.PeerId)
                {
                    continue;
                }

                if (peer.Endpoint.Matches(request.Endpoint))
                {
                    continue;
                }

                if (peer.Endpoint.AddressFamily == PeerAddressFamily.IPv4 && selectedV4 < peerCount)
                {
                    rentedV4[selectedV4++] = new SelectedPeer(peer.Endpoint, peer.PeerId);
                }
                else if (peer.Endpoint.AddressFamily == PeerAddressFamily.IPv6 && selectedV6 < peerCount)
                {
                    rentedV6[selectedV6++] = new SelectedPeer(peer.Endpoint, peer.PeerId);
                }
            }
        }

        if (selectedV4 == 0)
        {
            ArrayPool<SelectedPeer>.Shared.Return(rentedV4, clearArray: false);
        }

        if (selectedV6 == 0)
        {
            ArrayPool<SelectedPeer>.Shared.Return(rentedV6, clearArray: false);
        }

        if (selectedV4 == 0 && selectedV6 == 0)
        {
            return default;
        }

        var v4Result = selectedV4 > 0 ? new PeerSelectionResult(rentedV4, selectedV4, pooled: true) : default;
        var v6Result = selectedV6 > 0 ? new PeerSelectionResult(rentedV6, selectedV6, pooled: true) : default;
        return new AnnouncePeerSelection(v4Result, v6Result);
    }

    public SwarmCounts GetCounts(in InfoHashKey infoHash, DateTimeOffset now)
    {
        var swarm = GetExistingSwarm(infoHash);
        if (swarm is null)
        {
            return default;
        }

        var nowSeconds = now.ToUnixTimeSeconds();
        lock (swarm.SyncRoot)
        {
            if (nowSeconds >= swarm.NextSweepUnixSeconds)
            {
                PruneExpiredUnlocked(swarm, nowSeconds);
                swarm.NextSweepUnixSeconds = nowSeconds + _options.ExpirySweepIntervalSeconds;
            }

            return new SwarmCounts(swarm.SeederCount, swarm.LeecherCount, swarm.DownloadedCount);
        }
    }

    public void SweepExpired(DateTimeOffset now)
    {
        var nowSeconds = now.ToUnixTimeSeconds();

        foreach (var shard in _shards)
        {
            lock (shard.SyncRoot)
            {
                var empty = ArrayPool<InfoHashKey>.Shared.Rent(shard.Swarms.Count);
                var emptyCount = 0;

                foreach (var pair in shard.Swarms)
                {
                    lock (pair.Value.SyncRoot)
                    {
                        PruneExpiredUnlocked(pair.Value, nowSeconds);
                        pair.Value.NextSweepUnixSeconds = nowSeconds + _options.ExpirySweepIntervalSeconds;

                        if (pair.Value.Peers.Count == 0)
                        {
                            empty[emptyCount++] = pair.Key;
                        }
                    }
                }

                for (var i = 0; i < emptyCount; i++)
                {
                    shard.Swarms.Remove(empty[i]);
                }

                ArrayPool<InfoHashKey>.Shared.Return(empty, clearArray: false);
            }
        }
    }

    public long GetTotalPeerCount()
    {
        var total = 0L;
        foreach (var shard in _shards)
        {
            lock (shard.SyncRoot)
            {
                foreach (var swarm in shard.Swarms.Values)
                {
                    lock (swarm.SyncRoot)
                    {
                        total += swarm.Peers.Count;
                    }
                }
            }
        }

        return total;
    }

    public long GetTotalSwarmCount()
    {
        var total = 0L;
        foreach (var shard in _shards)
        {
            lock (shard.SyncRoot)
            {
                total += shard.Swarms.Count;
            }
        }

        return total;
    }

    public IEnumerable<(InfoHashKey InfoHash, SwarmCounts Counts)> EnumerateSwarms(DateTimeOffset now)
    {
        var nowSeconds = now.ToUnixTimeSeconds();
        var results = new List<(InfoHashKey, SwarmCounts)>(64);

        foreach (var shard in _shards)
        {
            lock (shard.SyncRoot)
            {
                foreach (var pair in shard.Swarms)
                {
                    lock (pair.Value.SyncRoot)
                    {
                        // Prune expired peers so counts are accurate in the snapshot.
                        if (nowSeconds >= pair.Value.NextSweepUnixSeconds)
                        {
                            PruneExpiredUnlocked(pair.Value, nowSeconds);
                            pair.Value.NextSweepUnixSeconds = nowSeconds + _options.ExpirySweepIntervalSeconds;
                        }

                        if (pair.Value.Peers.Count > 0)
                        {
                            results.Add((pair.Key, new SwarmCounts(
                                pair.Value.SeederCount,
                                pair.Value.LeecherCount,
                                pair.Value.DownloadedCount)));
                        }
                    }
                }
            }
        }

        return results;
    }

    private SwarmState GetOrCreateSwarm(in InfoHashKey infoHash)
    {
        var shard = GetShard(infoHash);
        lock (shard.SyncRoot)
        {
            if (!shard.Swarms.TryGetValue(infoHash, out var swarm))
            {
                swarm = new SwarmState();
                shard.Swarms.Add(infoHash, swarm);
            }

            return swarm;
        }
    }

    private SwarmState? GetExistingSwarm(in InfoHashKey infoHash)
    {
        var shard = GetShard(infoHash);
        lock (shard.SyncRoot)
        {
            return shard.Swarms.TryGetValue(infoHash, out var swarm) ? swarm : null;
        }
    }

    private SwarmShard GetShard(in InfoHashKey infoHash)
    {
        var shardIndex = (int)((uint)infoHash.GetHashCode() % (uint)_shards.Length);
        return _shards[shardIndex];
    }

    private static void PruneExpiredUnlocked(SwarmState swarm, long nowSeconds)
    {
        for (var index = swarm.Peers.Count - 1; index >= 0; index--)
        {
            if (swarm.Peers[index].ExpiresAtUnixSeconds <= nowSeconds)
            {
                RemovePeerAtUnlocked(swarm, index);
            }
        }
    }

    private static void RemovePeerUnlocked(SwarmState swarm, PeerIdKey peerId)
    {
        if (!swarm.IndexByPeerId.TryGetValue(peerId, out var index))
        {
            return;
        }

        RemovePeerAtUnlocked(swarm, index);
    }

    private static void RemovePeerAtUnlocked(SwarmState swarm, int index)
    {
        var removed = swarm.Peers[index];
        swarm.IndexByPeerId.Remove(removed.PeerId);

        if (removed.IsSeeder)
        {
            swarm.SeederCount--;
        }
        else
        {
            swarm.LeecherCount--;
        }

        var lastIndex = swarm.Peers.Count - 1;
        if (index != lastIndex)
        {
            var moved = swarm.Peers[lastIndex];
            swarm.Peers[index] = moved;
            swarm.IndexByPeerId[moved.PeerId] = index;
        }

        swarm.Peers.RemoveAt(lastIndex);
    }

    private static bool ShouldRecordCompleted(PeerState existing, in AnnounceRequest request)
    {
        return request.Event == TrackerEvent.Completed
            && request.IsSeeder
            && !existing.CompletionRecorded
            && !existing.IsSeeder
            && existing.Left > 0;
    }
}
