using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Swarmcore.BuildingBlocks.Abstractions.Hosting;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using Swarmcore.Contracts.Runtime;
using Swarmcore.Hosting;
using Tracker.CacheCoordinator.Application;
using Tracker.CacheCoordinator.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSwarmcoreInfrastructure(builder.Configuration, usePostgres: false, useRedis: true);
builder.Services.AddCacheCoordinatorInfrastructure();
builder.Services.AddHostedService<CacheCoordinatorStartupService>();
builder.Services.AddHostedService<NodeHeartbeatWorker>();

var host = builder.Build();
await host.RunAsync();

sealed class CacheCoordinatorStartupService(IServiceProvider serviceProvider, IReadinessState readinessState) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await StartupBootstrap.WaitForRedisAsync(serviceProvider, "cache-coordinator", cancellationToken);
        readinessState.MarkReady();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

sealed class NodeHeartbeatWorker(INodeHeartbeatRegistry nodeHeartbeatRegistry, IOptions<TrackerNodeOptions> trackerNodeOptions) : BackgroundService
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
