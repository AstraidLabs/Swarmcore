using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Swarmcore.Caching.Redis;
using Swarmcore.Contracts.Runtime;
using Tracker.CacheCoordinator.Application;

namespace Tracker.CacheCoordinator.Infrastructure;

public sealed class RedisNodeHeartbeatRegistry(IRedisCacheClient redisCacheClient) : INodeHeartbeatRegistry
{
    public Task PublishHeartbeatAsync(NodeHeartbeatDto heartbeat, CancellationToken cancellationToken)
    {
        return redisCacheClient.Database.StringSetAsync($"nodes:heartbeat:{heartbeat.NodeId}", JsonSerializer.Serialize(heartbeat), TimeSpan.FromSeconds(30));
    }
}

public static class CacheCoordinatorInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddCacheCoordinatorInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<INodeHeartbeatRegistry, RedisNodeHeartbeatRegistry>();
        return services;
    }
}
