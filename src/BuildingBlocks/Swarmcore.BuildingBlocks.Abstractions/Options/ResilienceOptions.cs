namespace Swarmcore.BuildingBlocks.Abstractions.Options;

/// <summary>
/// Options controlling operational resilience, degraded-mode behavior, and recovery.
/// </summary>
public sealed class ResilienceOptions
{
    public const string SectionName = "Swarmcore:Resilience";

    /// <summary>Whether to allow announce/scrape when Redis is unavailable (serve from L1 cache only). Default true.</summary>
    public bool AllowAnnounceWithoutRedis { get; init; } = true;

    /// <summary>Whether to allow announce/scrape when PostgreSQL is unavailable (serve from cache only). Default true.</summary>
    public bool AllowAnnounceWithoutPostgres { get; init; } = true;

    /// <summary>Whether to allow announce when the telemetry pipeline is impaired (drop telemetry, still serve). Default true.</summary>
    public bool AllowAnnounceWithoutTelemetry { get; init; } = true;

    /// <summary>Maximum consecutive Redis failures before entering degraded mode. Default 3.</summary>
    public int RedisFailureThreshold { get; init; } = 3;

    /// <summary>Maximum consecutive PostgreSQL failures before entering degraded mode. Default 3.</summary>
    public int PostgresFailureThreshold { get; init; } = 3;

    /// <summary>Seconds to wait before retrying a failed dependency during recovery. Default 5.</summary>
    public int DependencyRecoveryRetrySeconds { get; init; } = 5;

    /// <summary>Whether to attempt cache warmup from Redis on restart. Default true.</summary>
    public bool WarmupCacheOnRestart { get; init; } = true;

    /// <summary>Maximum number of cache entries to preload during warmup. Default 10000.</summary>
    public int MaxCacheWarmupEntries { get; init; } = 10000;

    /// <summary>Seconds to wait for cache warmup before marking ready anyway. Default 30.</summary>
    public int CacheWarmupTimeoutSeconds { get; init; } = 30;

    /// <summary>Whether stale node coordination data should degrade to local-only mode. Default true.</summary>
    public bool FallbackToLocalOnStaleCoordination { get; init; } = true;

    /// <summary>Seconds after which coordination data is considered stale. Default 120.</summary>
    public int CoordinationStalenessThresholdSeconds { get; init; } = 120;
}
