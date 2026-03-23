using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using Tracker.Gateway.Infrastructure;

namespace Tracker.Gateway.UnitTests;

public sealed class DegradedModeStateTests
{
    private static DegradedModeState CreateState(ResilienceOptions? options = null)
    {
        return new DegradedModeState(
            NullLogger<DegradedModeState>.Instance,
            Options.Create(options ?? new ResilienceOptions()));
    }

    [Fact]
    public void InitialState_IsFullyOperational()
    {
        var state = CreateState();
        var snapshot = state.Snapshot;

        Assert.True(snapshot.IsFullyOperational);
        Assert.False(snapshot.IsDegraded);
        Assert.True(snapshot.IsRedisAvailable);
        Assert.True(snapshot.IsPostgresAvailable);
        Assert.True(snapshot.IsTelemetryHealthy);
        Assert.True(snapshot.IsCoordinationFresh);
    }

    [Fact]
    public void RedisFailure_BelowThreshold_StaysOperational()
    {
        var state = CreateState(new ResilienceOptions { RedisFailureThreshold = 3 });

        state.RecordRedisFailure();
        state.RecordRedisFailure();

        var snapshot = state.Snapshot;
        Assert.True(snapshot.IsRedisAvailable);
        Assert.Equal(2, snapshot.ConsecutiveRedisFailures);
    }

    [Fact]
    public void RedisFailure_AtThreshold_EntersDegradedMode()
    {
        var state = CreateState(new ResilienceOptions { RedisFailureThreshold = 3 });

        state.RecordRedisFailure();
        state.RecordRedisFailure();
        state.RecordRedisFailure();

        var snapshot = state.Snapshot;
        Assert.False(snapshot.IsRedisAvailable);
        Assert.True(snapshot.IsDegraded);
        Assert.NotNull(snapshot.RedisUnavailableSince);
    }

    [Fact]
    public void RedisRecovery_ExitsDegradedMode()
    {
        var state = CreateState(new ResilienceOptions { RedisFailureThreshold = 1 });

        state.RecordRedisFailure();
        Assert.False(state.Snapshot.IsRedisAvailable);

        state.RecordRedisRecovery();
        Assert.True(state.Snapshot.IsRedisAvailable);
        Assert.Null(state.Snapshot.RedisUnavailableSince);
        Assert.Equal(0, state.Snapshot.ConsecutiveRedisFailures);
    }

    [Fact]
    public void PostgresFailure_AtThreshold_EntersDegradedMode()
    {
        var state = CreateState(new ResilienceOptions { PostgresFailureThreshold = 2 });

        state.RecordPostgresFailure();
        state.RecordPostgresFailure();

        var snapshot = state.Snapshot;
        Assert.False(snapshot.IsPostgresAvailable);
        Assert.True(snapshot.IsDegraded);
        Assert.NotNull(snapshot.PostgresUnavailableSince);
    }

    [Fact]
    public void PostgresRecovery_ExitsDegradedMode()
    {
        var state = CreateState(new ResilienceOptions { PostgresFailureThreshold = 1 });

        state.RecordPostgresFailure();
        Assert.False(state.Snapshot.IsPostgresAvailable);

        state.RecordPostgresRecovery();
        Assert.True(state.Snapshot.IsPostgresAvailable);
        Assert.Null(state.Snapshot.PostgresUnavailableSince);
    }

    [Fact]
    public void TelemetryImpairment_EntersDegradedMode()
    {
        var state = CreateState();

        state.RecordTelemetryImpairment();

        Assert.False(state.Snapshot.IsTelemetryHealthy);
        Assert.True(state.Snapshot.IsDegraded);
        Assert.NotNull(state.Snapshot.TelemetryImpairedSince);
    }

    [Fact]
    public void TelemetryRecovery_ExitsDegradedMode()
    {
        var state = CreateState();

        state.RecordTelemetryImpairment();
        state.RecordTelemetryRecovery();

        Assert.True(state.Snapshot.IsTelemetryHealthy);
        Assert.Null(state.Snapshot.TelemetryImpairedSince);
    }

    [Fact]
    public void CoordinationStaleness_EntersDegradedMode()
    {
        var state = CreateState();

        state.RecordCoordinationStaleness();

        Assert.False(state.Snapshot.IsCoordinationFresh);
        Assert.True(state.Snapshot.IsDegraded);
        Assert.NotNull(state.Snapshot.CoordinationStaleSince);
    }

    [Fact]
    public void CoordinationRecovery_ExitsDegradedMode()
    {
        var state = CreateState();

        state.RecordCoordinationStaleness();
        state.RecordCoordinationRecovery();

        Assert.True(state.Snapshot.IsCoordinationFresh);
        Assert.Null(state.Snapshot.CoordinationStaleSince);
    }

    [Fact]
    public void MultipleDegradations_AllTrackedIndependently()
    {
        var state = CreateState(new ResilienceOptions
        {
            RedisFailureThreshold = 1,
            PostgresFailureThreshold = 1
        });

        state.RecordRedisFailure();
        state.RecordPostgresFailure();
        state.RecordTelemetryImpairment();
        state.RecordCoordinationStaleness();

        var snapshot = state.Snapshot;
        Assert.False(snapshot.IsRedisAvailable);
        Assert.False(snapshot.IsPostgresAvailable);
        Assert.False(snapshot.IsTelemetryHealthy);
        Assert.False(snapshot.IsCoordinationFresh);
        Assert.True(snapshot.IsDegraded);
        Assert.False(snapshot.IsFullyOperational);
    }

    [Fact]
    public void PartialRecovery_StillDegraded()
    {
        var state = CreateState(new ResilienceOptions
        {
            RedisFailureThreshold = 1,
            PostgresFailureThreshold = 1
        });

        state.RecordRedisFailure();
        state.RecordPostgresFailure();

        // Recover Redis only
        state.RecordRedisRecovery();

        var snapshot = state.Snapshot;
        Assert.True(snapshot.IsRedisAvailable);
        Assert.False(snapshot.IsPostgresAvailable);
        Assert.True(snapshot.IsDegraded);
    }

    [Fact]
    public void FullRecovery_IsFullyOperational()
    {
        var state = CreateState(new ResilienceOptions
        {
            RedisFailureThreshold = 1,
            PostgresFailureThreshold = 1
        });

        state.RecordRedisFailure();
        state.RecordPostgresFailure();
        state.RecordTelemetryImpairment();
        state.RecordCoordinationStaleness();

        state.RecordRedisRecovery();
        state.RecordPostgresRecovery();
        state.RecordTelemetryRecovery();
        state.RecordCoordinationRecovery();

        var snapshot = state.Snapshot;
        Assert.True(snapshot.IsFullyOperational);
        Assert.False(snapshot.IsDegraded);
    }

    [Fact]
    public void RepeatedRecovery_DoesNotThrow()
    {
        var state = CreateState();

        // Recovery when already healthy should be idempotent
        state.RecordRedisRecovery();
        state.RecordPostgresRecovery();
        state.RecordTelemetryRecovery();
        state.RecordCoordinationRecovery();

        Assert.True(state.Snapshot.IsFullyOperational);
    }
}

public sealed class ConfigurationRestoreValidatorTests
{
    [Fact]
    public void ValidSnapshot_PassesValidation()
    {
        var snapshot = new ConfigurationStateSnapshot(
            DateTimeOffset.UtcNow,
            "1.0",
            [new ExportedTorrentPolicy("ABC123", true, true, 1800, 900, 50, 80, true, 1, null)],
            5,
            [new ExportedUserPermission(Guid.NewGuid(), true, true, true, true, 1)],
            [new ExportedBanRule("torrent", "abc", "test", null, 1)]);

        var errors = ConfigurationRestoreValidator.Validate(snapshot);
        Assert.Empty(errors);
    }

    [Fact]
    public void InvalidSchemaVersion_Fails()
    {
        var snapshot = new ConfigurationStateSnapshot(
            DateTimeOffset.UtcNow,
            "2.0",
            [],
            0,
            [],
            []);

        var errors = ConfigurationRestoreValidator.Validate(snapshot);
        Assert.Contains(errors, e => e.Contains("Unsupported schema version"));
    }

    [Fact]
    public void EmptyInfoHash_Fails()
    {
        var snapshot = new ConfigurationStateSnapshot(
            DateTimeOffset.UtcNow,
            "1.0",
            [new ExportedTorrentPolicy("", true, true, 1800, 900, 50, 80, true, 1, null)],
            0,
            [],
            []);

        var errors = ConfigurationRestoreValidator.Validate(snapshot);
        Assert.Contains(errors, e => e.Contains("empty info_hash"));
    }

    [Fact]
    public void NegativeAnnounceInterval_Fails()
    {
        var snapshot = new ConfigurationStateSnapshot(
            DateTimeOffset.UtcNow,
            "1.0",
            [new ExportedTorrentPolicy("ABC", true, true, -1, 900, 50, 80, true, 1, null)],
            0,
            [],
            []);

        var errors = ConfigurationRestoreValidator.Validate(snapshot);
        Assert.Contains(errors, e => e.Contains("Invalid announce interval"));
    }

    [Fact]
    public void MinIntervalExceedsAnnounceInterval_Fails()
    {
        var snapshot = new ConfigurationStateSnapshot(
            DateTimeOffset.UtcNow,
            "1.0",
            [new ExportedTorrentPolicy("ABC", true, true, 900, 1800, 50, 80, true, 1, null)],
            0,
            [],
            []);

        var errors = ConfigurationRestoreValidator.Validate(snapshot);
        Assert.Contains(errors, e => e.Contains("Min announce interval exceeds"));
    }

    [Fact]
    public void EmptyBanScope_Fails()
    {
        var snapshot = new ConfigurationStateSnapshot(
            DateTimeOffset.UtcNow,
            "1.0",
            [],
            0,
            [],
            [new ExportedBanRule("", "subject", "reason", null, 1)]);

        var errors = ConfigurationRestoreValidator.Validate(snapshot);
        Assert.Contains(errors, e => e.Contains("empty scope"));
    }

    [Fact]
    public void EmptyUserId_Fails()
    {
        var snapshot = new ConfigurationStateSnapshot(
            DateTimeOffset.UtcNow,
            "1.0",
            [],
            0,
            [new ExportedUserPermission(Guid.Empty, true, true, true, true, 1)],
            []);

        var errors = ConfigurationRestoreValidator.Validate(snapshot);
        Assert.Contains(errors, e => e.Contains("empty UserId"));
    }
}

public sealed class GatewayDependencyStateTests
{
    [Fact]
    public void InitialState_IsUnhealthy()
    {
        var state = new GatewayDependencyState();
        var snapshot = state.Snapshot;

        Assert.False(snapshot.Redis.IsHealthy);
        Assert.False(snapshot.Postgres.IsHealthy);
    }

    [Fact]
    public void UpdateRedis_ReflectsInSnapshot()
    {
        var state = new GatewayDependencyState();
        var now = DateTimeOffset.UtcNow;

        state.UpdateRedis(true, now);

        var snapshot = state.Snapshot;
        Assert.True(snapshot.Redis.IsHealthy);
        Assert.Equal(now, snapshot.Redis.CheckedAtUtc);
    }

    [Fact]
    public void UpdatePostgres_ReflectsInSnapshot()
    {
        var state = new GatewayDependencyState();
        var now = DateTimeOffset.UtcNow;

        state.UpdatePostgres(true, now, "ok");

        var snapshot = state.Snapshot;
        Assert.True(snapshot.Postgres.IsHealthy);
        Assert.Equal("ok", snapshot.Postgres.Detail);
    }

    [Fact]
    public void FailedDependency_ReportsUnhealthy()
    {
        var state = new GatewayDependencyState();
        var now = DateTimeOffset.UtcNow;

        state.UpdateRedis(false, now, "ConnectionException");

        var snapshot = state.Snapshot;
        Assert.False(snapshot.Redis.IsHealthy);
        Assert.Equal("ConnectionException", snapshot.Redis.Detail);
    }
}
