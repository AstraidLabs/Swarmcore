using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using Swarmcore.BuildingBlocks.Observability.Diagnostics;

namespace Tracker.Gateway.Infrastructure;

/// <summary>
/// Tracks degraded-mode state for the gateway with explicit entry/exit transitions,
/// observability, and bounded fallback behavior.
/// </summary>
public interface IDegradedModeState
{
    DegradedModeSnapshot Snapshot { get; }
    void RecordRedisFailure();
    void RecordRedisRecovery();
    void RecordPostgresFailure();
    void RecordPostgresRecovery();
    void RecordTelemetryImpairment();
    void RecordTelemetryRecovery();
    void RecordCoordinationStaleness();
    void RecordCoordinationRecovery();
}

public sealed record DegradedModeSnapshot(
    bool IsRedisAvailable,
    bool IsPostgresAvailable,
    bool IsTelemetryHealthy,
    bool IsCoordinationFresh,
    int ConsecutiveRedisFailures,
    int ConsecutivePostgresFailures,
    DateTimeOffset? RedisUnavailableSince,
    DateTimeOffset? PostgresUnavailableSince,
    DateTimeOffset? TelemetryImpairedSince,
    DateTimeOffset? CoordinationStaleSince)
{
    public bool IsFullyOperational => IsRedisAvailable && IsPostgresAvailable && IsTelemetryHealthy && IsCoordinationFresh;
    public bool IsDegraded => !IsFullyOperational;
}

public sealed class DegradedModeState : IDegradedModeState
{
    private readonly ILogger<DegradedModeState> _logger;
    private readonly ResilienceOptions _options;

    private volatile bool _redisAvailable = true;
    private volatile bool _postgresAvailable = true;
    private volatile bool _telemetryHealthy = true;
    private volatile bool _coordinationFresh = true;
    private int _consecutiveRedisFailures;
    private int _consecutivePostgresFailures;
    private DateTimeOffset? _redisUnavailableSince;
    private DateTimeOffset? _postgresUnavailableSince;
    private DateTimeOffset? _telemetryImpairedSince;
    private DateTimeOffset? _coordinationStaleSince;

    public DegradedModeState(
        ILogger<DegradedModeState> logger,
        IOptions<ResilienceOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public DegradedModeSnapshot Snapshot => new(
        _redisAvailable,
        _postgresAvailable,
        _telemetryHealthy,
        _coordinationFresh,
        Volatile.Read(ref _consecutiveRedisFailures),
        Volatile.Read(ref _consecutivePostgresFailures),
        _redisUnavailableSince,
        _postgresUnavailableSince,
        _telemetryImpairedSince,
        _coordinationStaleSince);

    public void RecordRedisFailure()
    {
        var failures = Interlocked.Increment(ref _consecutiveRedisFailures);
        TrackerDiagnostics.RedisFailures.Add(1);

        if (failures >= _options.RedisFailureThreshold && _redisAvailable)
        {
            _redisAvailable = false;
            _redisUnavailableSince = DateTimeOffset.UtcNow;
            TrackerDiagnostics.DegradedModeEntered.Add(1, new KeyValuePair<string, object?>("dependency", "redis"));
            _logger.LogWarning(
                "Degraded mode ENTERED: Redis unavailable after {Failures} consecutive failures.",
                failures);
        }
    }

    public void RecordRedisRecovery()
    {
        if (!_redisAvailable)
        {
            var unavailableDuration = _redisUnavailableSince.HasValue
                ? DateTimeOffset.UtcNow - _redisUnavailableSince.Value
                : TimeSpan.Zero;
            TrackerDiagnostics.DegradedModeExited.Add(1, new KeyValuePair<string, object?>("dependency", "redis"));
            _logger.LogInformation(
                "Degraded mode EXITED: Redis recovered. Downtime={Downtime}.",
                unavailableDuration);
        }

        _redisAvailable = true;
        _redisUnavailableSince = null;
        Interlocked.Exchange(ref _consecutiveRedisFailures, 0);
    }

    public void RecordPostgresFailure()
    {
        var failures = Interlocked.Increment(ref _consecutivePostgresFailures);
        TrackerDiagnostics.PostgresFailures.Add(1);

        if (failures >= _options.PostgresFailureThreshold && _postgresAvailable)
        {
            _postgresAvailable = false;
            _postgresUnavailableSince = DateTimeOffset.UtcNow;
            TrackerDiagnostics.DegradedModeEntered.Add(1, new KeyValuePair<string, object?>("dependency", "postgres"));
            _logger.LogWarning(
                "Degraded mode ENTERED: PostgreSQL unavailable after {Failures} consecutive failures.",
                failures);
        }
    }

    public void RecordPostgresRecovery()
    {
        if (!_postgresAvailable)
        {
            var unavailableDuration = _postgresUnavailableSince.HasValue
                ? DateTimeOffset.UtcNow - _postgresUnavailableSince.Value
                : TimeSpan.Zero;
            TrackerDiagnostics.DegradedModeExited.Add(1, new KeyValuePair<string, object?>("dependency", "postgres"));
            _logger.LogInformation(
                "Degraded mode EXITED: PostgreSQL recovered. Downtime={Downtime}.",
                unavailableDuration);
        }

        _postgresAvailable = true;
        _postgresUnavailableSince = null;
        Interlocked.Exchange(ref _consecutivePostgresFailures, 0);
    }

    public void RecordTelemetryImpairment()
    {
        if (_telemetryHealthy)
        {
            _telemetryHealthy = false;
            _telemetryImpairedSince = DateTimeOffset.UtcNow;
            TrackerDiagnostics.DegradedModeEntered.Add(1, new KeyValuePair<string, object?>("dependency", "telemetry"));
            _logger.LogWarning("Degraded mode ENTERED: Telemetry pipeline impaired.");
        }
    }

    public void RecordTelemetryRecovery()
    {
        if (!_telemetryHealthy)
        {
            TrackerDiagnostics.DegradedModeExited.Add(1, new KeyValuePair<string, object?>("dependency", "telemetry"));
            _logger.LogInformation("Degraded mode EXITED: Telemetry pipeline recovered.");
        }

        _telemetryHealthy = true;
        _telemetryImpairedSince = null;
    }

    public void RecordCoordinationStaleness()
    {
        if (_coordinationFresh)
        {
            _coordinationFresh = false;
            _coordinationStaleSince = DateTimeOffset.UtcNow;
            TrackerDiagnostics.DegradedModeEntered.Add(1, new KeyValuePair<string, object?>("dependency", "coordination"));
            _logger.LogWarning("Degraded mode ENTERED: Node coordination data is stale.");
        }
    }

    public void RecordCoordinationRecovery()
    {
        if (!_coordinationFresh)
        {
            TrackerDiagnostics.DegradedModeExited.Add(1, new KeyValuePair<string, object?>("dependency", "coordination"));
            _logger.LogInformation("Degraded mode EXITED: Node coordination data is fresh.");
        }

        _coordinationFresh = true;
        _coordinationStaleSince = null;
    }
}
