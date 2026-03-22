using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Swarmcore.BuildingBlocks.Abstractions.Time;
using Tracker.Gateway.Application.Announce;

namespace Tracker.Gateway.Runtime;

public sealed class PeerMutationService(IRuntimeSwarmStore runtimeSwarmStore) : IPeerMutationService
{
    public SwarmCounts Apply(in AnnounceRequest request, int announceIntervalSeconds, DateTimeOffset now)
    {
        var ttl = TimeSpan.FromSeconds(Math.Max(announceIntervalSeconds * 2, 1800));
        return runtimeSwarmStore.ApplyMutation(request, ttl, now);
    }
}

public sealed class PeerSelectionService(IRuntimeSwarmStore runtimeSwarmStore) : IPeerSelectionService
{
    public AnnouncePeerSelection Select(in AnnounceRequest request, int requestedPeers, int maxPeers, DateTimeOffset now)
    {
        var effectivePeerCount = Math.Clamp(requestedPeers <= 0 ? 0 : requestedPeers, 0, maxPeers);
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

        services.AddSingleton<IRuntimeSwarmStore, PartitionedRuntimeSwarmStore>();
        services.AddSingleton<IPeerMutationService, PeerMutationService>();
        services.AddSingleton<IPeerSelectionService, PeerSelectionService>();
        services.AddHostedService<PeerExpiryBackgroundService>();
        return services;
    }
}
