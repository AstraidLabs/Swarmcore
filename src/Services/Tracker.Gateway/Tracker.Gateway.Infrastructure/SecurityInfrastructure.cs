using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using StackExchange.Redis;
using BeeTracker.BuildingBlocks.Abstractions.Options;
using BeeTracker.BuildingBlocks.Observability.Diagnostics;
using BeeTracker.Caching.Redis;
using BeeTracker.Persistence.Postgres;
using Tracker.Gateway.Application.Announce;

namespace Tracker.Gateway.Infrastructure;

public sealed class TrackerPasskeyRedactor : IPasskeyRedactor
{
    public string? Redact(string? passkey)
    {
        if (string.IsNullOrWhiteSpace(passkey))
        {
            return null;
        }

        if (passkey.Length <= 6)
        {
            return "***";
        }

        return $"{passkey[..3]}***{passkey[^3..]}";
    }
}

internal sealed class FixedWindowCounter
{
    private long _window;
    private int _count;

    public long LastWindow => Volatile.Read(ref _window);

    public bool TryIncrement(long window, int limit)
    {
        while (true)
        {
            var currentWindow = Volatile.Read(ref _window);
            if (currentWindow != window)
            {
                if (Interlocked.CompareExchange(ref _window, window, currentWindow) == currentWindow)
                {
                    Interlocked.Exchange(ref _count, 0);
                }

                continue;
            }

            var count = Interlocked.Increment(ref _count);
            return count <= limit;
        }
    }

    public static void SweepStaleEntries<TKey>(ConcurrentDictionary<TKey, FixedWindowCounter> counters, long staleBefore) where TKey : notnull
    {
        foreach (var pair in counters)
        {
            if (pair.Value.LastWindow < staleBefore)
            {
                counters.TryRemove(pair.Key, out _);
            }
        }
    }
}

public sealed class AnnounceAbuseGuard(IOptions<TrackerAbuseProtectionOptions> options) : IAnnounceAbuseGuard
{
    private readonly ConcurrentDictionary<string, FixedWindowCounter> _passkeyCounters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, FixedWindowCounter> _ipCounters = new(StringComparer.Ordinal);
    private long _lastSweepWindow;

    public AnnounceError? Evaluate(HttpContext httpContext, in AnnounceRequest request)
    {
        var nowWindow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (nowWindow - Volatile.Read(ref _lastSweepWindow) >= 60)
        {
            Volatile.Write(ref _lastSweepWindow, nowWindow);
            var staleBefore = nowWindow - 10;
            FixedWindowCounter.SweepStaleEntries(_passkeyCounters, staleBefore);
            FixedWindowCounter.SweepStaleEntries(_ipCounters, staleBefore);
        }

        if (options.Value.EnableAnnouncePasskeyRateLimit && !string.IsNullOrWhiteSpace(request.Passkey))
        {
            var counter = _passkeyCounters.GetOrAdd(request.Passkey, static _ => new FixedWindowCounter());
            if (!counter.TryIncrement(nowWindow, options.Value.AnnouncePerPasskeyPerSecond))
            {
                TrackerDiagnostics.AbuseThrottled.Add(1, new KeyValuePair<string, object?>("type", "passkey"));
                return new AnnounceError(StatusCodes.Status429TooManyRequests, "announce throttled");
            }
        }

        if (options.Value.EnableAnnounceIpRateLimit)
        {
            var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var ipCounter = _ipCounters.GetOrAdd(ip, static _ => new FixedWindowCounter());
            if (!ipCounter.TryIncrement(nowWindow, options.Value.AnnouncePerIpPerSecond))
            {
                TrackerDiagnostics.AbuseThrottled.Add(1, new KeyValuePair<string, object?>("type", "ip"));
                return new AnnounceError(StatusCodes.Status429TooManyRequests, "announce throttled");
            }
        }

        return null;
    }
}

public sealed class ScrapeAbuseGuard(IOptions<TrackerAbuseProtectionOptions> options) : IScrapeAbuseGuard
{
    private readonly ConcurrentDictionary<string, FixedWindowCounter> _ipCounters = new(StringComparer.Ordinal);
    private long _lastSweepWindow;

    public AnnounceError? Evaluate(HttpContext httpContext)
    {
        if (!options.Value.EnableScrapeIpRateLimit)
        {
            return null;
        }

        var nowWindow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (nowWindow - Volatile.Read(ref _lastSweepWindow) >= 60)
        {
            Volatile.Write(ref _lastSweepWindow, nowWindow);
            FixedWindowCounter.SweepStaleEntries(_ipCounters, nowWindow - 10);
        }
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var counter = _ipCounters.GetOrAdd(ip, static _ => new FixedWindowCounter());
        if (!counter.TryIncrement(nowWindow, options.Value.ScrapePerIpPerSecond))
        {
            TrackerDiagnostics.AbuseThrottled.Add(1, new KeyValuePair<string, object?>("type", "ip"));
            return new AnnounceError(StatusCodes.Status429TooManyRequests, "scrape throttled");
        }

        return null;
    }
}

public sealed record DependencyStatus(bool IsHealthy, DateTimeOffset CheckedAtUtc, string? Detail);

public sealed record GatewayDependencySnapshot(DependencyStatus Redis, DependencyStatus Postgres);

public interface IGatewayDependencyState
{
    GatewayDependencySnapshot Snapshot { get; }
    void UpdateRedis(bool isHealthy, DateTimeOffset checkedAtUtc, string? detail = null);
    void UpdatePostgres(bool isHealthy, DateTimeOffset checkedAtUtc, string? detail = null);
}

public sealed class GatewayDependencyState : IGatewayDependencyState
{
    private DependencyStatus _redis = new(false, DateTimeOffset.MinValue, "not checked");
    private DependencyStatus _postgres = new(false, DateTimeOffset.MinValue, "not checked");

    public GatewayDependencySnapshot Snapshot => new(
        Volatile.Read(ref _redis),
        Volatile.Read(ref _postgres));

    public void UpdateRedis(bool isHealthy, DateTimeOffset checkedAtUtc, string? detail = null)
    {
        Volatile.Write(ref _redis, new DependencyStatus(isHealthy, checkedAtUtc, detail));
    }

    public void UpdatePostgres(bool isHealthy, DateTimeOffset checkedAtUtc, string? detail = null)
    {
        Volatile.Write(ref _postgres, new DependencyStatus(isHealthy, checkedAtUtc, detail));
    }
}

public sealed class GatewayDependencyMonitorService(
    IGatewayDependencyState dependencyState,
    IRedisCacheClient redisCacheClient,
    IPostgresConnectionFactory postgresConnectionFactory,
    ILogger<GatewayDependencyMonitorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var checkedAtUtc = DateTimeOffset.UtcNow;
            await CheckRedisAsync(checkedAtUtc, stoppingToken);
            await CheckPostgresAsync(checkedAtUtc, stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task CheckRedisAsync(DateTimeOffset checkedAtUtc, CancellationToken cancellationToken)
    {
        try
        {
            await redisCacheClient.Database.PingAsync();
            dependencyState.UpdateRedis(true, checkedAtUtc);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Redis dependency check failed.");
            dependencyState.UpdateRedis(false, checkedAtUtc, exception.GetType().Name);
        }
    }

    private async Task CheckPostgresAsync(DateTimeOffset checkedAtUtc, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await postgresConnectionFactory.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand("select 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            dependencyState.UpdatePostgres(true, checkedAtUtc);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "PostgreSQL dependency check failed.");
            dependencyState.UpdatePostgres(false, checkedAtUtc, exception.GetType().Name);
        }
    }
}

public sealed class TrackerRequestGuardMiddleware(
    RequestDelegate next,
    IOptions<TrackerSecurityOptions> securityOptions,
    IPasskeyRedactor passkeyRedactor,
    ILogger<TrackerRequestGuardMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext httpContext, IBencodeResponseWriter bencodeResponseWriter)
    {
        if (!IsTrackerProtocolRequest(httpContext.Request.Path))
        {
            await next(httpContext);
            return;
        }

        if (!HttpMethods.IsGet(httpContext.Request.Method))
        {
            TrackerDiagnostics.RequestMalformed.Add(1, new KeyValuePair<string, object?>("reason", "method_not_allowed"));
            await bencodeResponseWriter.WriteFailureAsync(httpContext.Response, StatusCodes.Status405MethodNotAllowed, "method not allowed", httpContext.RequestAborted);
            return;
        }

        var maxQueryLength = httpContext.Request.Path.StartsWithSegments("/scrape", StringComparison.OrdinalIgnoreCase)
            ? securityOptions.Value.ScrapeMaxQueryLength
            : securityOptions.Value.AnnounceMaxQueryLength;

        var queryLength = httpContext.Request.QueryString.HasValue ? httpContext.Request.QueryString.Value!.Length : 0;
        if (queryLength > maxQueryLength)
        {
            TrackerDiagnostics.RequestMalformed.Add(1, new KeyValuePair<string, object?>("reason", "query_too_large"));
            logger.LogWarning("Rejected tracker request with oversized query. Path={Path}", RedactPath(httpContext.Request.Path, passkeyRedactor));
            await bencodeResponseWriter.WriteFailureAsync(httpContext.Response, StatusCodes.Status400BadRequest, "query too large", httpContext.RequestAborted);
            return;
        }

        if (!securityOptions.Value.AllowPasskeyInQueryString && QueryContainsPasskey(httpContext.Request.QueryString.Value))
        {
            TrackerDiagnostics.RequestMalformed.Add(1, new KeyValuePair<string, object?>("reason", "passkey_in_query"));
            logger.LogWarning("Rejected tracker request with querystring passkey. Path={Path}", RedactPath(httpContext.Request.Path, passkeyRedactor));
            await bencodeResponseWriter.WriteFailureAsync(httpContext.Response, StatusCodes.Status400BadRequest, "passkey query routing is disabled", httpContext.RequestAborted);
            return;
        }

        await next(httpContext);
    }

    private static bool IsTrackerProtocolRequest(PathString path)
    {
        return path.StartsWithSegments("/announce", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/scrape", StringComparison.OrdinalIgnoreCase);
    }

    private static bool QueryContainsPasskey(string? query)
    {
        return !string.IsNullOrWhiteSpace(query)
            && query.Contains("passkey=", StringComparison.OrdinalIgnoreCase);
    }

    private static string RedactPath(PathString path, IPasskeyRedactor passkeyRedactor)
    {
        var value = path.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            return "/";
        }

        var lastSlash = value.LastIndexOf('/');
        if (lastSlash <= 0 || lastSlash == value.Length - 1)
        {
            return value;
        }

        var candidate = value[(lastSlash + 1)..];
        return $"{value[..(lastSlash + 1)]}{passkeyRedactor.Redact(candidate)}";
    }
}
