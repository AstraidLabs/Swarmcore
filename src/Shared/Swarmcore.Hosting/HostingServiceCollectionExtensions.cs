using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Swarmcore.BuildingBlocks.Abstractions.Hosting;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using Swarmcore.BuildingBlocks.Abstractions.Time;
using Swarmcore.BuildingBlocks.Infrastructure.Hosting;
using Swarmcore.BuildingBlocks.Infrastructure.Time;
using Swarmcore.Caching.Redis;
using Swarmcore.Persistence.Postgres;

namespace Swarmcore.Hosting;

public static class HostingServiceCollectionExtensions
{
    public static IServiceCollection AddSwarmcoreHostFoundation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IReadinessState, ReadinessState>();
        services.AddSingleton<IClock, SystemClock>();

        services.AddOptions<TrackerNodeOptions>()
            .Bind(configuration.GetSection(TrackerNodeOptions.SectionName))
            .Validate(static options => !string.IsNullOrWhiteSpace(options.NodeId), "NodeId is required.")
            .ValidateOnStart();

        services.AddOptions<TelemetryBatchingOptions>()
            .Bind(configuration.GetSection(TelemetryBatchingOptions.SectionName))
            .Validate(static options => options.BatchSize > 0, "BatchSize must be positive.")
            .Validate(static options => options.FlushIntervalMilliseconds > 0, "FlushIntervalMilliseconds must be positive.")
            .ValidateOnStart();

        services.AddOptions<PolicyCacheOptions>()
            .Bind(configuration.GetSection(PolicyCacheOptions.SectionName))
            .Validate(static options => options.L1Seconds > 0, "L1Seconds must be positive.")
            .Validate(static options => options.L2Seconds > 0, "L2Seconds must be positive.")
            .ValidateOnStart();

        services.AddOptions<TrackerSecurityOptions>()
            .Bind(configuration.GetSection(TrackerSecurityOptions.SectionName))
            .Validate(static options => options.AnnounceMaxQueryLength > 0, "Announce max query length must be positive.")
            .Validate(static options => options.ScrapeMaxQueryLength > 0, "Scrape max query length must be positive.")
            .Validate(static options => options.HardMaxNumWant > 0, "Hard max numwant must be positive.")
            .Validate(static options => options.MaxScrapeInfoHashes > 0, "Max scrape info hashes must be positive.")
            .ValidateOnStart();

        services.AddOptions<TrackerAbuseProtectionOptions>()
            .Bind(configuration.GetSection(TrackerAbuseProtectionOptions.SectionName))
            .Validate(static options => options.AnnouncePerPasskeyPerSecond > 0, "Announce per-passkey rate limit must be positive.")
            .Validate(static options => options.AnnouncePerIpPerSecond > 0, "Announce per-IP rate limit must be positive.")
            .Validate(static options => options.ScrapePerIpPerSecond > 0, "Scrape per-IP rate limit must be positive.")
            .ValidateOnStart();

        services.AddOptions<TrustedProxyOptions>()
            .Bind(configuration.GetSection(TrustedProxyOptions.SectionName))
            .Validate(static options => options.ForwardLimit > 0, "Forward limit must be positive.")
            .ValidateOnStart();

        services.AddOptions<DependencyDegradationOptions>()
            .Bind(configuration.GetSection(DependencyDegradationOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<StartupBootstrapOptions>()
            .Bind(configuration.GetSection(StartupBootstrapOptions.SectionName))
            .Validate(static options => options.MaxAttempts > 0, "Bootstrap max attempts must be positive.")
            .Validate(static options => options.RetryDelaySeconds > 0, "Bootstrap retry delay must be positive.")
            .ValidateOnStart();

        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return services;
    }

    public static IServiceCollection AddSwarmcoreInfrastructure(this IServiceCollection services, IConfiguration configuration, bool usePostgres, bool useRedis)
    {
        services.AddSwarmcoreHostFoundation(configuration);

        if (usePostgres)
        {
            services.AddPostgresFoundation(configuration);
        }

        if (useRedis)
        {
            services.AddRedisCaching(configuration);
        }

        return services;
    }

    public static IEndpointRouteBuilder MapCommonHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = static check => check.Tags.Contains("live")
        });

        endpoints.MapGet("/health/ready", (IReadinessState readiness) =>
            readiness.IsReady ? Results.Ok(new { status = "ready" }) : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

        endpoints.MapGet("/health/startup", (IReadinessState readiness) =>
            readiness.IsReady ? Results.Ok(new { status = "started" }) : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

        return endpoints;
    }
}
