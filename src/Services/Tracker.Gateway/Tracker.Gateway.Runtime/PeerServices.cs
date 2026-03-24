using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using BeeTracker.BuildingBlocks.Abstractions.Options;
using BeeTracker.BuildingBlocks.Abstractions.Time;
using BeeTracker.BuildingBlocks.Observability.Diagnostics;
using Tracker.Gateway.Application.Announce;
using Tracker.Gateway.Application.Cluster;

namespace Tracker.Gateway.Runtime;

public sealed class PeerMutationService(IRuntimeSwarmStore runtimeSwarmStore) : IPeerMutationService
{
    public SwarmCounts Apply(in AnnounceRequest request, int announceIntervalSeconds, DateTimeOffset now)
    {
        var ttl = TimeSpan.FromSeconds(Math.Max(announceIntervalSeconds * 2, 1800));
        return runtimeSwarmStore.ApplyMutation(request, ttl, now);
    }
}

/// <summary>
/// Cluster-aware peer selection service.
///
/// Architecture decision: local-first with cluster locality tracking.
///
/// Each node serves announces from its own local peer store regardless of which
/// node owns the cluster shard for the swarm. This keeps the hot-path fast and
/// avoids distributed state on the critical path.
///
/// The IShardRouter is used to:
///   1. Determine whether the request is for a locally-owned swarm (locality tag).
///   2. Emit request locality metrics so operators can tune load balancer routing.
///
/// In a correctly-configured cluster, the load balancer uses consistent hash routing
/// so that traffic for a given info_hash lands on the owning node most of the time.
/// Non-owning node traffic ("non-local") is expected but tracked.
/// </summary>
public sealed class PeerSelectionService(IRuntimeSwarmStore runtimeSwarmStore, IShardRouter shardRouter) : IPeerSelectionService
{
    public AnnouncePeerSelection Select(in AnnounceRequest request, int requestedPeers, int maxPeers, DateTimeOffset now)
    {
        var effectivePeerCount = Math.Clamp(requestedPeers <= 0 ? 0 : requestedPeers, 0, maxPeers);

        // Track request locality: is this swarm owned by the local node?
        var locality = shardRouter.IsLocallyOwned(request.InfoHash) ? "local" : "non_local";
        TrackerDiagnostics.ClusterRequestLocality.Add(1, new KeyValuePair<string, object?>("locality", locality));

        return runtimeSwarmStore.SelectPeers(request, effectivePeerCount, now);
    }
}

public sealed class PeerExpiryBackgroundService(
    IRuntimeSwarmStore runtimeSwarmStore,
    IClock clock,
    IOptions<GatewayRuntimeOptions> options,
    ILogger<PeerExpiryBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                runtimeSwarmStore.SweepExpired(clock.UtcNow);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error sweeping expired peers.");
            }

            await Task.Delay(TimeSpan.FromSeconds(options.Value.ExpirySweepIntervalSeconds), stoppingToken);
        }
    }
}

public static class GatewayRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayRuntime(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GatewayRuntimeOptions>()
            .Bind(configuration.GetSection(GatewayRuntimeOptions.SectionName))
            .Validate(static options => options.ShardCount > 0, "ShardCount must be positive.")
            .Validate(static options => options.MaxPeersPerResponse > 0, "MaxPeersPerResponse must be positive.")
            .Validate(static options => options.PeerTtlSeconds >= 60, "PeerTtlSeconds must be at least 60.")
            .ValidateOnStart();

        services.AddOptions<ClusterShardingOptions>()
            .Bind(configuration.GetSection(ClusterShardingOptions.SectionName))
            .Validate(static opts => opts.ClusterShardCount > 0, "ClusterShardCount must be positive.")
            .Validate(static opts => opts.OwnershipRefreshIntervalSeconds < opts.OwnershipLeaseDurationSeconds,
                "OwnershipRefreshIntervalSeconds must be less than OwnershipLeaseDurationSeconds.")
            .ValidateOnStart();

        services.AddSingleton<IRuntimeSwarmStore, PartitionedRuntimeSwarmStore>();
        services.AddSingleton<IPeerMutationService, PeerMutationService>();
        services.AddSingleton<IPeerSelectionService, PeerSelectionService>();
        services.AddHostedService<PeerExpiryBackgroundService>();

        // Cluster shard routing (local in-memory ownership cache, no Redis dependency in Runtime)
        services.AddClusterShardRouting();

        return services;
    }
}
