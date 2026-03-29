using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tracker.UdpTracker.Protocol;

namespace Tracker.UdpTracker.Service;

public static class UdpTrackerServiceCollectionExtensions
{
    public static IServiceCollection AddUdpTracker(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<UdpTrackerOptions>()
            .Bind(configuration.GetSection(UdpTrackerOptions.SectionName))
            .Validate(static options => !string.IsNullOrWhiteSpace(options.BindAddress), "UDP bind address must be configured.")
            .Validate(static options => options.Port is > 0 and <= 65535, "UDP tracker port must be between 1 and 65535.")
            .Validate(static options => options.ConnectionTimeoutSeconds > 0, "ConnectionTimeoutSeconds must be positive.")
            .Validate(static options => options.MaxDatagramSize >= 512, "MaxDatagramSize must be at least 512 bytes.")
            .Validate(static options => options.ReceiveBufferSize >= 1024, "ReceiveBufferSize must be at least 1024 bytes.")
            .Validate(static options => options.MaxScrapeInfoHashes > 0, "MaxScrapeInfoHashes must be positive.")
            .Validate(static options => options.ConnectionIdSweepIntervalSeconds > 0, "ConnectionIdSweepIntervalSeconds must be positive.")
            .ValidateOnStart();

        services.AddSingleton<ConnectionIdManager>();
        services.AddSingleton<UdpTrackerRequestHandler>();
        services.AddHostedService<UdpTrackerListener>();

        return services;
    }
}
