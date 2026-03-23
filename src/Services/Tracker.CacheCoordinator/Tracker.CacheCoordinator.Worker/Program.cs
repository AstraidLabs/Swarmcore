using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Swarmcore.BuildingBlocks.Abstractions.Hosting;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using Swarmcore.BuildingBlocks.Observability.Diagnostics;
using Swarmcore.Contracts.Runtime;
using Swarmcore.Hosting;
using Tracker.CacheCoordinator.Application;
using Tracker.CacheCoordinator.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSwarmcoreInfrastructure(builder.Configuration, usePostgres: false, useRedis: true);
builder.Services.AddCacheCoordinatorInfrastructure();
builder.Services.AddClusterCoordinatorInfrastructure();
builder.Services.AddHostedService<CacheCoordinatorStartupService>();
builder.Services.AddHostedService<NodeHeartbeatWorker>();
builder.Services.AddHostedService<ShardOwnershipWorker>();

var host = builder.Build();
await host.RunAsync();

// ─── Startup ──────────────────────────────────────────────────────────────────

sealed class CacheCoordinatorStartupService(IServiceProvider serviceProvider, IReadinessState readinessState) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await StartupBootstrap.WaitForRedisAsync(serviceProvider, "cache-coordinator", cancellationToken);
        readinessState.MarkReady();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

// ─── Heartbeat Worker ─────────────────────────────────────────────────────────

sealed class NodeHeartbeatWorker(
    INodeHeartbeatRegistry nodeHeartbeatRegistry,
    IOptions<TrackerNodeOptions> trackerNodeOptions) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await nodeHeartbeatRegistry.PublishHeartbeatAsync(new NodeHeartbeatDto(
                trackerNodeOptions.Value.NodeId,
                trackerNodeOptions.Value.Region,
                DateTimeOffset.UtcNow), stoppingToken);

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}

// ─── Shard Ownership Worker ───────────────────────────────────────────────────

/// <summary>
/// Manages shard ownership lifecycle for this node.
///
/// On startup: claims all available (unowned or expired) shards.
/// On interval: refreshes leases for owned shards, then claims any newly available shards
///              (e.g., another node's leases expired — failover recovery).
/// On drain/stop: releases all owned shards gracefully.
///
/// Metrics: owned shard count gauge, ownership claims, refresh failures, failover events.
/// </summary>
sealed class ShardOwnershipWorker(
    IShardOwnershipService ownershipService,
    INodeOperationalStateService operationalStateService,
    IReadinessState readinessState,
    IOptions<ClusterShardingOptions> shardingOptions,
    IOptions<TrackerNodeOptions> nodeOptions,
    ILogger<ShardOwnershipWorker> logger) : BackgroundService
{
    private readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(shardingOptions.Value.OwnershipRefreshIntervalSeconds);
    private readonly int _totalShards = shardingOptions.Value.ClusterShardCount;
    private readonly string _nodeId = nodeOptions.Value.NodeId;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for startup readiness before claiming shards
        while (!readinessState.IsReady && !stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
        }

        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        // Register cluster ownership gauges with metrics
        TrackerDiagnostics.RegisterClusterOwnershipGauges(
            ownedShardsCallback: () => ownershipService.OwnedShardIds.Count,
            totalShardsCallback: () => _totalShards);

        // Initial shard claim: grab all unclaimed shards
        logger.LogInformation("Node {NodeId} starting shard ownership claim (total shards: {Total}).", _nodeId, _totalShards);
        var claimed = await ownershipService.ClaimAvailableShardsAsync(stoppingToken);
        logger.LogInformation("Node {NodeId} claimed {Claimed} shards on startup.", _nodeId, claimed);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_refreshInterval, stoppingToken);
                await RunOwnershipCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Shard ownership cycle failed; will retry.");
            }
        }

        // Graceful shutdown: release all owned shards only if draining
        // (if killed abruptly, leases will expire naturally per OwnershipLeaseDurationSeconds)
        if (operationalStateService.CurrentState is NodeOperationalState.Draining or NodeOperationalState.Maintenance)
        {
            logger.LogInformation("Node {NodeId} releasing all shards during {State} shutdown.", _nodeId, operationalStateService.CurrentState);
            using var releaseTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await ownershipService.ReleaseAllShardsAsync(releaseTimeout.Token);
        }
        else
        {
            logger.LogInformation("Node {NodeId} stopping without drain; {Count} shards will expire naturally.",
                _nodeId, ownershipService.OwnedShardIds.Count);
        }
    }

    private async Task RunOwnershipCycleAsync(CancellationToken cancellationToken)
    {
        // Step 1: Refresh leases for currently owned shards
        var lostCount = await ownershipService.RefreshOwnedShardsAsync(cancellationToken);
        if (lostCount > 0)
        {
            logger.LogWarning("Node {NodeId} lost ownership of {Lost} shards during refresh.", _nodeId, lostCount);
        }

        // Step 2: Claim any newly available shards (including failover from dead nodes)
        var newlyClaimed = await ownershipService.ClaimAvailableShardsAsync(cancellationToken);
        if (newlyClaimed > 0)
        {
            TrackerDiagnostics.ClusterFailoverEvents.Add(newlyClaimed,
                new KeyValuePair<string, object?>("node_id", _nodeId),
                new KeyValuePair<string, object?>("reason", "lease_expired"));

            logger.LogInformation("Node {NodeId} claimed {Claimed} additional shards (failover).", _nodeId, newlyClaimed);
        }
    }
}
