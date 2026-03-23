using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using Swarmcore.BuildingBlocks.Observability.Diagnostics;
using Swarmcore.Caching.Redis;
using Swarmcore.Persistence.Postgres;

namespace Tracker.Gateway.Infrastructure;

/// <summary>
/// Enhanced dependency monitor that integrates with degraded-mode state tracking.
/// Provides explicit diagnostics for dependency failure impact and recovery transitions.
/// </summary>
public sealed class ResilientDependencyMonitorService(
    IGatewayDependencyState dependencyState,
    IDegradedModeState degradedModeState,
    IRedisCacheClient redisCacheClient,
    IPostgresConnectionFactory postgresConnectionFactory,
    IOptions<ResilienceOptions> resilienceOptions,
    ILogger<ResilientDependencyMonitorService> logger) : BackgroundService
{
    private int _recoveryAttempt;

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
            degradedModeState.RecordRedisRecovery();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Redis dependency check failed.");
            dependencyState.UpdateRedis(false, checkedAtUtc, exception.GetType().Name);
            degradedModeState.RecordRedisFailure();
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
            degradedModeState.RecordPostgresRecovery();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "PostgreSQL dependency check failed.");
            dependencyState.UpdatePostgres(false, checkedAtUtc, exception.GetType().Name);
            degradedModeState.RecordPostgresFailure();
        }
    }
}

/// <summary>
/// Configuration state export/import service for backup and restore readiness.
/// Exports durable configuration state (policies, passkeys, permissions, bans) to JSON.
/// Runtime ephemeral state (peer store, swarm counts) is explicitly excluded.
/// </summary>
public static class ConfigurationStateExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    /// <summary>
    /// Exports all configuration state from PostgreSQL to a JSON-serializable snapshot.
    /// This includes: torrent policies, passkeys (masked), permissions, ban rules.
    /// Excludes: runtime peer state, telemetry, audit logs, ephemeral cache state.
    /// </summary>
    public static async Task<ConfigurationStateSnapshot> ExportAsync(
        IPostgresConnectionFactory postgresConnectionFactory,
        CancellationToken cancellationToken)
    {
        TrackerDiagnostics.ConfigExportOperations.Add(1);
        await using var connection = await postgresConnectionFactory.OpenConnectionAsync(cancellationToken);

        var policies = new List<ExportedTorrentPolicy>();
        await using (var cmd = new NpgsqlCommand(
            """
            select t.info_hash, t.is_private, t.is_enabled,
                   coalesce(tp.announce_interval_seconds, 1800),
                   coalesce(tp.min_announce_interval_seconds, 900),
                   coalesce(tp.default_numwant, 50),
                   coalesce(tp.max_numwant, 80),
                   coalesce(tp.allow_scrape, true),
                   coalesce(tp.row_version, 1),
                   tp.warning_message
            from torrents t
            left join torrent_policies tp on tp.torrent_id = t.id
            order by t.info_hash
            """, connection))
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                policies.Add(new ExportedTorrentPolicy(
                    reader.GetString(0),
                    reader.GetBoolean(1),
                    reader.GetBoolean(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetInt32(6),
                    reader.GetBoolean(7),
                    reader.GetInt64(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9)));
            }
        }

        var permissions = new List<ExportedUserPermission>();
        await using (var cmd = new NpgsqlCommand(
            """
            select user_id, can_leech, can_seed, can_scrape, can_use_private_tracker, row_version
            from permissions
            order by user_id
            """, connection))
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                permissions.Add(new ExportedUserPermission(
                    reader.GetGuid(0),
                    reader.GetBoolean(1),
                    reader.GetBoolean(2),
                    reader.GetBoolean(3),
                    reader.GetBoolean(4),
                    reader.GetInt64(5)));
            }
        }

        var bans = new List<ExportedBanRule>();
        await using (var cmd = new NpgsqlCommand(
            """
            select scope, subject, reason, expires_at_utc, row_version
            from bans
            order by scope, subject
            """, connection))
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                bans.Add(new ExportedBanRule(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : new DateTimeOffset(reader.GetFieldValue<DateTime>(3), TimeSpan.Zero),
                    reader.GetInt64(4)));
            }
        }

        var passkeyCount = 0;
        await using (var cmd = new NpgsqlCommand("select count(*) from passkeys", connection))
        {
            passkeyCount = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
        }

        return new ConfigurationStateSnapshot(
            DateTimeOffset.UtcNow,
            "1.0",
            policies,
            passkeyCount,
            permissions,
            bans);
    }

    public static string SerializeSnapshot(ConfigurationStateSnapshot snapshot)
        => JsonSerializer.Serialize(snapshot, JsonOptions);
}

public sealed record ConfigurationStateSnapshot(
    DateTimeOffset ExportedAtUtc,
    string SchemaVersion,
    IReadOnlyList<ExportedTorrentPolicy> TorrentPolicies,
    int PasskeyCount,
    IReadOnlyList<ExportedUserPermission> UserPermissions,
    IReadOnlyList<ExportedBanRule> BanRules);

public sealed record ExportedTorrentPolicy(
    string InfoHash,
    bool IsPrivate,
    bool IsEnabled,
    int AnnounceIntervalSeconds,
    int MinAnnounceIntervalSeconds,
    int DefaultNumWant,
    int MaxNumWant,
    bool AllowScrape,
    long Version,
    string? WarningMessage);

public sealed record ExportedUserPermission(
    Guid UserId,
    bool CanLeech,
    bool CanSeed,
    bool CanScrape,
    bool CanUsePrivateTracker,
    long Version);

public sealed record ExportedBanRule(
    string Scope,
    string Subject,
    string Reason,
    DateTimeOffset? ExpiresAtUtc,
    long Version);

/// <summary>
/// Validates configuration state on restore to ensure consistency.
/// </summary>
public static class ConfigurationRestoreValidator
{
    public static IReadOnlyList<string> Validate(ConfigurationStateSnapshot snapshot)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(snapshot.SchemaVersion))
        {
            errors.Add("Missing schema version in snapshot.");
        }

        if (snapshot.SchemaVersion != "1.0")
        {
            errors.Add($"Unsupported schema version: {snapshot.SchemaVersion}. Expected 1.0.");
        }

        foreach (var policy in snapshot.TorrentPolicies)
        {
            if (string.IsNullOrWhiteSpace(policy.InfoHash))
            {
                errors.Add("Torrent policy with empty info_hash found.");
            }

            if (policy.AnnounceIntervalSeconds <= 0)
            {
                errors.Add($"Invalid announce interval for {policy.InfoHash}: {policy.AnnounceIntervalSeconds}.");
            }

            if (policy.MinAnnounceIntervalSeconds <= 0)
            {
                errors.Add($"Invalid min announce interval for {policy.InfoHash}: {policy.MinAnnounceIntervalSeconds}.");
            }

            if (policy.MinAnnounceIntervalSeconds > policy.AnnounceIntervalSeconds)
            {
                errors.Add($"Min announce interval exceeds announce interval for {policy.InfoHash}.");
            }
        }

        foreach (var permission in snapshot.UserPermissions)
        {
            if (permission.UserId == Guid.Empty)
            {
                errors.Add("User permission with empty UserId found.");
            }
        }

        foreach (var ban in snapshot.BanRules)
        {
            if (string.IsNullOrWhiteSpace(ban.Scope))
            {
                errors.Add("Ban rule with empty scope found.");
            }

            if (string.IsNullOrWhiteSpace(ban.Subject))
            {
                errors.Add("Ban rule with empty subject found.");
            }
        }

        return errors;
    }
}
