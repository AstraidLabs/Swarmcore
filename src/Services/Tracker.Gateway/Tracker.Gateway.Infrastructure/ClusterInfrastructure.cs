using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using Swarmcore.BuildingBlocks.Observability.Diagnostics;
using Swarmcore.Caching.Redis;
using Swarmcore.Contracts.Runtime;
using Tracker.Gateway.Application.Announce;
using Tracker.Gateway.Application.Cluster;
using Tracker.Gateway.Runtime;

namespace Tracker.Gateway.Infrastructure;

// ─── Distributed Swarm Summary Store ─────────────────────────────────────────

public sealed class RedisSwarmSummaryStore(
    IRedisCacheClient redisCacheClient,
    IOptions<ClusterShardingOptions> options) : ISwarmSummaryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(options.Value.SwarmSummaryTtlSeconds);

    private static string SummaryKey(string infoHashHex) => $"cluster:swarm:summary:{infoHashHex}";

    public Task PublishAsync(SwarmSummaryDto summary, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(summary, JsonOptions);
        return redisCacheClient.Database.StringSetAsync(SummaryKey(summary.InfoHash), json, _ttl);
    }

    public async Task<SwarmSummaryDto?> GetAsync(string infoHashHex, CancellationToken cancellationToken)
    {
        var value = await redisCacheClient.Database.StringGetAsync(SummaryKey(infoHashHex));
        if (!value.HasValue)
        {
            return null;
        }

        return JsonSerializer.Deserialize<SwarmSummaryDto>(value.ToString(), JsonOptions);
    }

    public async Task PublishBatchAsync(IReadOnlyList<SwarmSummaryDto> summaries, CancellationToken cancellationToken)
    {
        if (summaries.Count == 0)
        {
            return;
        }

        var batch = redisCacheClient.Database.CreateBatch();
        var tasks = new Task[summaries.Count];

        for (var i = 0; i < summaries.Count; i++)
        {
            var summary = summaries[i];
            var json = JsonSerializer.Serialize(summary, JsonOptions);
            tasks[i] = batch.StringSetAsync(SummaryKey(summary.InfoHash), json, _ttl);
        }

        batch.Execute();
        await Task.WhenAll(tasks);

        TrackerDiagnostics.ClusterSwarmSummariesPublished.Add(summaries.Count);
    }
}

// ─── Node Operational State Store ─────────────────────────────────────────────

public sealed class RedisNodeOperationalStateStore(
    IRedisCacheClient redisCacheClient,
    IConnectionMultiplexer connectionMultiplexer) : INodeOperationalStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan StateTtl = TimeSpan.FromHours(24);

    private static string StateKey(string nodeId) => $"cluster:node:state:{nodeId}";

    public async Task<NodeOperationalStateDto?> GetStateAsync(string nodeId, CancellationToken cancellationToken)
    {
        var value = await redisCacheClient.Database.StringGetAsync(StateKey(nodeId));
        if (!value.HasValue)
        {
            return null;
        }

        return JsonSerializer.Deserialize<NodeOperationalStateDto>(value.ToString(), JsonOptions);
    }

    public Task SetStateAsync(NodeOperationalStateDto state, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions);
        return redisCacheClient.Database.StringSetAsync(StateKey(state.NodeId), json, StateTtl);
    }

    public async Task<IReadOnlyCollection<NodeOperationalStateDto>> GetAllStatesAsync(CancellationToken cancellationToken)
    {
        var results = new List<NodeOperationalStateDto>(16);
        var endpoints = connectionMultiplexer.GetEndPoints();

        foreach (var endpoint in endpoints)
        {
            var server = connectionMultiplexer.GetServer(endpoint);
            if (!server.IsConnected)
            {
                continue;
            }

            foreach (var key in server.Keys(pattern: "cluster:node:state:*"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var value = await redisCacheClient.Database.StringGetAsync(key);
                if (!value.HasValue)
                {
                    continue;
                }

                var dto = JsonSerializer.Deserialize<NodeOperationalStateDto>(value.ToString(), JsonOptions);
                if (dto is not null)
                {
                    results.Add(dto);
                }
            }
        }

        return results;
    }
}

// ─── Ownership Cache Refresher (Background Service) ──────────────────────────

/// <summary>
/// Background service that periodically reads shard ownership data from Redis
/// and updates the local IShardOwnershipCache.
///
/// Runs on the gateway. Does not claim or release ownership itself.
/// </summary>
public sealed class ShardOwnershipCacheRefresherService(
    LocalShardOwnershipCache ownershipCache,
    IRedisCacheClient redisCacheClient,
    IOptions<ClusterShardingOptions> shardingOptions,
    IOptions<TrackerNodeOptions> nodeOptions,
    ILogger<ShardOwnershipCacheRefresherService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly int _totalShards = shardingOptions.Value.ClusterShardCount;
    private readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(shardingOptions.Value.OwnershipCacheRefreshIntervalSeconds);
    private readonly string _localNodeId = nodeOptions.Value.NodeId;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Brief initial delay to allow ownership workers to claim before first read
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to refresh shard ownership cache; will retry.");
            }

            await Task.Delay(_refreshInterval, stoppingToken);
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var ownerships = new Dictionary<int, string>(_totalShards);

        for (var shardId = 0; shardId < _totalShards; shardId++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = ShardOwnershipRedisKeys.OwnershipKey(shardId);
            var value = await redisCacheClient.Database.StringGetAsync(key);
            if (!value.HasValue)
            {
                continue;
            }

            var record = JsonSerializer.Deserialize<ShardOwnershipRecord>(value.ToString(), JsonOptions);
            if (record is not null)
            {
                ownerships[shardId] = record.OwnerNodeId;
            }
        }

        ownershipCache.Update(_localNodeId, ownerships);
    }
}

// ─── Swarm Summary Publisher Background Service ───────────────────────────────

/// <summary>
/// Background service running on each gateway node that periodically publishes
/// swarm summaries for all locally-owned swarms to the distributed cache.
///
/// Only the owning node for a swarm publishes its summary. Non-owning nodes can
/// read these summaries for accurate scrape responses and cross-node visibility.
/// </summary>
public sealed class SwarmSummaryPublisherService(
    IRuntimeSwarmStore runtimeSwarmStore,
    IShardRouter shardRouter,
    ISwarmSummaryStore summaryStore,
    IOptions<ClusterShardingOptions> shardingOptions,
    IOptions<TrackerNodeOptions> nodeOptions,
    ILogger<SwarmSummaryPublisherService> logger) : BackgroundService
{
    private readonly TimeSpan _publishInterval = TimeSpan.FromSeconds(shardingOptions.Value.SwarmSummaryPublishIntervalSeconds);
    private readonly string _localNodeId = nodeOptions.Value.NodeId;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Stagger publication relative to ownership refresh to reduce Redis pressure
        await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishSummariesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to publish swarm summaries; will retry.");
            }

            await Task.Delay(_publishInterval, stoppingToken);
        }
    }

    private async Task PublishSummariesAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var summaries = new List<SwarmSummaryDto>(256);

        // Snapshot swarm counts from the local runtime store.
        // The store provides an enumeration of all active swarms with their counts.
        foreach (var (infoHash, counts) in runtimeSwarmStore.EnumerateSwarms(now))
        {
            // Only publish summaries for locally-owned swarms.
            // Non-owning nodes should not overwrite the owning node's summary.
            if (!shardRouter.IsLocallyOwned(infoHash))
            {
                continue;
            }

            summaries.Add(new SwarmSummaryDto(
                infoHash.ToHexString(),
                _localNodeId,
                counts.SeederCount,
                counts.LeecherCount,
                counts.DownloadedCount,
                now));
        }

        if (summaries.Count > 0)
        {
            await summaryStore.PublishBatchAsync(summaries, cancellationToken);
            logger.LogDebug("Published {Count} swarm summaries to distributed cache.", summaries.Count);
        }
    }
}

// ─── Service Registration ─────────────────────────────────────────────────────

public static class ClusterInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayClusterInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ISwarmSummaryStore, RedisSwarmSummaryStore>();
        services.AddSingleton<INodeOperationalStateStore, RedisNodeOperationalStateStore>();
        services.AddHostedService<ShardOwnershipCacheRefresherService>();
        services.AddHostedService<SwarmSummaryPublisherService>();
        return services;
    }
}

/// <summary>Shared Redis key scheme for cluster shard ownership, used by both gateway and coordinator.</summary>
public static class ShardOwnershipRedisKeys
{
    public static string OwnershipKey(int shardId) => $"cluster:shard:{shardId}:owner";
    public static string NodeStateKey(string nodeId) => $"cluster:node:state:{nodeId}";
    public static string SwarmSummaryKey(string infoHashHex) => $"cluster:swarm:summary:{infoHashHex}";
}
