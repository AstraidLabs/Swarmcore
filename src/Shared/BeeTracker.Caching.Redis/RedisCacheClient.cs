using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using BeeTracker.BuildingBlocks.Abstractions.Options;

namespace BeeTracker.Caching.Redis;

public interface IRedisCacheClient
{
    IDatabase Database { get; }
    ISubscriber Subscriber { get; }
}

public sealed class RedisCacheClient(IConnectionMultiplexer multiplexer) : IRedisCacheClient
{
    public IDatabase Database => multiplexer.GetDatabase();
    public ISubscriber Subscriber => multiplexer.GetSubscriber();
}

public static class RedisServiceCollectionExtensions
{
    public static IServiceCollection AddRedisCaching(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RedisOptions>()
            .Bind(configuration.GetSection(RedisOptions.SectionName))
            .Validate(static options => !string.IsNullOrWhiteSpace(options.Configuration), "Redis configuration is required.")
            .ValidateOnStart();

        services.AddSingleton<IConnectionMultiplexer>(static serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<RedisOptions>>().Value;
            return ConnectionMultiplexer.Connect(options.Configuration);
        });

        services.AddSingleton<IRedisCacheClient, RedisCacheClient>();

        return services;
    }
}
