using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using BeeTracker.BuildingBlocks.Abstractions.Options;
using BeeTracker.BuildingBlocks.Observability.Diagnostics;
using BeeTracker.Caching.Redis;
using BeeTracker.Persistence.Postgres;
using Audit.Application;
using Audit.Domain;
using Tracker.Gateway.Application.Announce;

namespace Tracker.Gateway.Infrastructure;

/// <summary>
/// Volatile in-memory governance state. Reads are lock-free via Volatile.Read.
/// Updates are applied atomically via snapshot replacement.
/// No Redis or database dependency on the hot path.
/// </summary>
public sealed class RuntimeGovernanceStateService : IRuntimeGovernanceState
{
    private RuntimeGovernanceSnapshot _snapshot;

    public RuntimeGovernanceStateService(IOptions<TrackerGovernanceOptions> governanceOptions, IOptions<TrackerCompatibilityOptions> compatibilityOptions)
    {
        var gov = governanceOptions.Value;
        var compat = compatibilityOptions.Value;
        _snapshot = new RuntimeGovernanceSnapshot(
            gov.AnnounceDisabled,
            gov.ScrapeDisabled,
            gov.GlobalMaintenanceMode,
            gov.ReadOnlyMode,
            gov.EmergencyAbuseMitigation,
            gov.UdpDisabled,
            gov.IPv6Frozen,
            gov.PolicyFreezeMode,
            compat.CompatibilityMode,
            compat.StrictnessProfile);
    }

    private RuntimeGovernanceSnapshot Current => Volatile.Read(ref _snapshot);

    public bool AnnounceDisabled => Current.AnnounceDisabled;
    public bool ScrapeDisabled => Current.ScrapeDisabled;
    public bool GlobalMaintenanceMode => Current.GlobalMaintenanceMode;
    public bool ReadOnlyMode => Current.ReadOnlyMode;
    public bool EmergencyAbuseMitigation => Current.EmergencyAbuseMitigation;
    public bool UdpDisabled => Current.UdpDisabled;
    public bool IPv6Frozen => Current.IPv6Frozen;
    public bool PolicyFreezeMode => Current.PolicyFreezeMode;
    public ClientCompatibilityMode EffectiveCompatibilityMode => Current.CompatibilityMode;
    public ProtocolStrictnessProfile EffectiveStrictnessProfile => Current.StrictnessProfile;

    public RuntimeGovernanceSnapshot GetSnapshot() => Current;

    public RuntimeGovernanceSnapshot Apply(RuntimeGovernanceUpdate update)
    {
        var current = Current;
        var next = new RuntimeGovernanceSnapshot(
            update.AnnounceDisabled ?? current.AnnounceDisabled,
            update.ScrapeDisabled ?? current.ScrapeDisabled,
            update.GlobalMaintenanceMode ?? current.GlobalMaintenanceMode,
            update.ReadOnlyMode ?? current.ReadOnlyMode,
            update.EmergencyAbuseMitigation ?? current.EmergencyAbuseMitigation,
            update.UdpDisabled ?? current.UdpDisabled,
            update.IPv6Frozen ?? current.IPv6Frozen,
            update.PolicyFreezeMode ?? current.PolicyFreezeMode,
            update.CompatibilityMode ?? current.CompatibilityMode,
            update.StrictnessProfile ?? current.StrictnessProfile);
        Volatile.Write(ref _snapshot, next);
        return next;
    }

    /// <summary>
    /// Replaces the entire snapshot atomically. Used for startup recovery and pub/sub refresh.
    /// </summary>
    public void ReplaceSnapshot(RuntimeGovernanceSnapshot snapshot)
    {
        Volatile.Write(ref _snapshot, snapshot);
    }
}

// ─── Governance Redis Persistence ───────────────────────────────────────────

/// <summary>
/// Redis key scheme and serialization for governance state persistence.
/// </summary>
public static class GovernanceRedisKeys
{
    public const string GovernanceStateKey = "governance:state";
    public const string GovernanceUpdateChannel = "governance:state:updated";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(RuntimeGovernanceSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, SerializerOptions);

    public static RuntimeGovernanceSnapshot? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        return JsonSerializer.Deserialize<RuntimeGovernanceSnapshot>(json, SerializerOptions);
    }
}

/// <summary>
/// Persists governance state to Redis and publishes update notifications.
/// Does NOT touch the hot path. Called only on admin governance mutations.
/// </summary>
public sealed class GovernancePersistenceService(
    IRedisCacheClient redisClient,
    ILogger<GovernancePersistenceService> logger)
{
    /// <summary>
    /// Persists the governance snapshot to Redis and publishes an update notification.
    /// </summary>
    public async Task PersistAndPublishAsync(RuntimeGovernanceSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var json = GovernanceRedisKeys.Serialize(snapshot);

        try
        {
            await redisClient.Database.StringSetAsync(
                GovernanceRedisKeys.GovernanceStateKey,
                json);

            await redisClient.Subscriber.PublishAsync(
                GovernanceRedisKeys.GovernanceUpdateChannel,
                json);

            logger.LogInformation("Governance state persisted to Redis and update published.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist governance state to Redis.");
            throw;
        }
    }

    /// <summary>
    /// Loads the governance snapshot from Redis. Returns null if no persisted state exists.
    /// </summary>
    public async Task<RuntimeGovernanceSnapshot?> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await redisClient.Database.StringGetAsync(GovernanceRedisKeys.GovernanceStateKey);
            if (json.IsNullOrEmpty)
            {
                logger.LogDebug("No persisted governance state found in Redis.");
                return null;
            }

            var snapshot = GovernanceRedisKeys.Deserialize(json!);
            if (snapshot is not null)
            {
                logger.LogInformation("Governance state loaded from Redis.");
            }

            return snapshot;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load governance state from Redis. Falling back to config defaults.");
            return null;
        }
    }
}

/// <summary>
/// Audits governance state changes via the async audit channel.
/// Produces detailed per-field change records and one summary record.
/// </summary>
public sealed class GovernanceAuditService(IAuditChannelWriter auditChannelWriter)
{
    public void AuditGovernanceChange(
        RuntimeGovernanceSnapshot before,
        RuntimeGovernanceSnapshot after,
        string? actorId,
        string? ipAddress,
        string? userAgent,
        string? correlationId)
    {
        var metadataJson = JsonSerializer.Serialize(new
        {
            before = GovernanceRedisKeys.Serialize(before),
            after = GovernanceRedisKeys.Serialize(after),
        });

        // Summary audit record for the overall governance change
        auditChannelWriter.TryWrite(AuditRecord.Create(
            AuditAction.GovernanceUpdated,
            actorId,
            targetUserId: null,
            correlationId,
            ipAddress,
            userAgent,
            AuditOutcome.Success,
            metadataJson: metadataJson));

        // Per-field change records for granular audit trail
        if (before.AnnounceDisabled != after.AnnounceDisabled)
            WriteFieldChange(AuditAction.GovernanceAnnounceDisabledChanged, before.AnnounceDisabled, after.AnnounceDisabled, actorId, ipAddress, userAgent, correlationId);
        if (before.ScrapeDisabled != after.ScrapeDisabled)
            WriteFieldChange(AuditAction.GovernanceScrapeDisabledChanged, before.ScrapeDisabled, after.ScrapeDisabled, actorId, ipAddress, userAgent, correlationId);
        if (before.GlobalMaintenanceMode != after.GlobalMaintenanceMode)
            WriteFieldChange(AuditAction.GovernanceMaintenanceModeChanged, before.GlobalMaintenanceMode, after.GlobalMaintenanceMode, actorId, ipAddress, userAgent, correlationId);
        if (before.ReadOnlyMode != after.ReadOnlyMode)
            WriteFieldChange(AuditAction.GovernanceReadOnlyModeChanged, before.ReadOnlyMode, after.ReadOnlyMode, actorId, ipAddress, userAgent, correlationId);
        if (before.EmergencyAbuseMitigation != after.EmergencyAbuseMitigation)
            WriteFieldChange(AuditAction.GovernanceEmergencyAbuseMitigationChanged, before.EmergencyAbuseMitigation, after.EmergencyAbuseMitigation, actorId, ipAddress, userAgent, correlationId);
        if (before.UdpDisabled != after.UdpDisabled)
            WriteFieldChange(AuditAction.GovernanceUdpDisabledChanged, before.UdpDisabled, after.UdpDisabled, actorId, ipAddress, userAgent, correlationId);
        if (before.IPv6Frozen != after.IPv6Frozen)
            WriteFieldChange(AuditAction.GovernanceIPv6FrozenChanged, before.IPv6Frozen, after.IPv6Frozen, actorId, ipAddress, userAgent, correlationId);
        if (before.PolicyFreezeMode != after.PolicyFreezeMode)
            WriteFieldChange(AuditAction.GovernancePolicyFreezeModeChanged, before.PolicyFreezeMode, after.PolicyFreezeMode, actorId, ipAddress, userAgent, correlationId);
        if (before.CompatibilityMode != after.CompatibilityMode)
            WriteFieldChange(AuditAction.GovernanceCompatibilityModeChanged, before.CompatibilityMode, after.CompatibilityMode, actorId, ipAddress, userAgent, correlationId);
        if (before.StrictnessProfile != after.StrictnessProfile)
            WriteFieldChange(AuditAction.GovernanceStrictnessProfileChanged, before.StrictnessProfile, after.StrictnessProfile, actorId, ipAddress, userAgent, correlationId);
    }

    public void AuditGovernanceRestored(RuntimeGovernanceSnapshot restored, string source)
    {
        var metadataJson = JsonSerializer.Serialize(new
        {
            source,
            state = GovernanceRedisKeys.Serialize(restored),
        });

        auditChannelWriter.TryWrite(AuditRecord.Create(
            AuditAction.GovernanceRestored,
            actorId: "system",
            targetUserId: null,
            correlationId: null,
            ipAddress: null,
            userAgent: null,
            AuditOutcome.Success,
            reasonCode: $"restored_from_{source}",
            metadataJson: metadataJson));
    }

    private void WriteFieldChange<T>(
        string action, T before, T after,
        string? actorId, string? ipAddress, string? userAgent, string? correlationId)
    {
        var metadataJson = JsonSerializer.Serialize(new { before = before?.ToString(), after = after?.ToString() });
        auditChannelWriter.TryWrite(AuditRecord.Create(
            action,
            actorId,
            targetUserId: null,
            correlationId,
            ipAddress,
            userAgent,
            AuditOutcome.Success,
            metadataJson: metadataJson));
    }
}

/// <summary>
/// Subscribes to Redis governance update notifications.
/// When another node (or the admin service proxy) persists a governance change,
/// this service refreshes the local in-memory snapshot without any hot-path cost.
/// </summary>
public sealed class GovernanceRefreshBackgroundService(
    IRedisCacheClient redisClient,
    RuntimeGovernanceStateService governanceStateService,
    IOptions<TrackerNodeOptions> nodeOptions,
    ILogger<GovernanceRefreshBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Governance refresh subscriber starting.");

        try
        {
            await redisClient.Subscriber.SubscribeAsync(
                GovernanceRedisKeys.GovernanceUpdateChannel,
                (_, message) =>
                {
                    try
                    {
                        var snapshot = GovernanceRedisKeys.Deserialize(message!);
                        if (snapshot is not null)
                        {
                            governanceStateService.ReplaceSnapshot(snapshot);
                            logger.LogDebug("Governance state refreshed from pub/sub notification on node {NodeId}.", nodeOptions.Value.NodeId);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to deserialize governance update from pub/sub.");
                    }
                });

            // Keep the background service alive until cancellation.
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Governance refresh subscriber encountered a fatal error.");
        }
        finally
        {
            try
            {
                await redisClient.Subscriber.UnsubscribeAsync(GovernanceRedisKeys.GovernanceUpdateChannel);
            }
            catch
            {
                // Best-effort unsubscribe on shutdown.
            }

            logger.LogInformation("Governance refresh subscriber stopped.");
        }
    }
}

/// <summary>
/// Restores governance state from Redis on startup, before the gateway accepts traffic.
/// Falls back to config defaults if Redis is unavailable or has no persisted state.
/// </summary>
public sealed class GovernanceStartupRecoveryService(
    RuntimeGovernanceStateService governanceStateService,
    GovernancePersistenceService persistenceService,
    GovernanceAuditService auditService,
    ILogger<GovernanceStartupRecoveryService> logger)
{
    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        var persisted = await persistenceService.LoadAsync(cancellationToken);
        if (persisted is not null)
        {
            governanceStateService.ReplaceSnapshot(persisted);
            auditService.AuditGovernanceRestored(persisted, "redis");
            logger.LogInformation("Governance state recovered from Redis on startup.");
        }
        else
        {
            var fallback = governanceStateService.GetSnapshot();
            logger.LogInformation("No persisted governance state found. Using config defaults: " +
                "AnnounceDisabled={AnnounceDisabled}, MaintenanceMode={MaintenanceMode}, ReadOnlyMode={ReadOnlyMode}.",
                fallback.AnnounceDisabled, fallback.GlobalMaintenanceMode, fallback.ReadOnlyMode);
        }
    }
}

/// <summary>
/// Advanced abuse guard with combined IP + passkey scoring, anomaly detection,
/// and structured abuse diagnostics. Extends basic rate limiting with behavioral analysis.
/// Emits significant abuse events asynchronously via the abuse event channel.
/// </summary>
public sealed class AdvancedAbuseGuard
{
    private readonly ConcurrentDictionary<string, AbuseScore> _ipScores = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AbuseScore> _passkeyScores = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AbuseScore> _combinedScores = new(StringComparer.Ordinal);
    private long _lastSweepWindow;

    private readonly IAbuseEventChannelWriter? _eventWriter;
    private readonly string _nodeId;

    public AdvancedAbuseGuard() : this(null, null) { }

    public AdvancedAbuseGuard(IAbuseEventChannelWriter? eventWriter, IOptions<TrackerNodeOptions>? nodeOptions)
    {
        _eventWriter = eventWriter;
        _nodeId = nodeOptions?.Value.NodeId ?? System.Environment.MachineName;
    }

    public void RecordMalformedRequest(string ip, string? passkey)
    {
        var now = DateTimeOffset.UtcNow;
        GetOrCreateScore(_ipScores, ip, now).MalformedRequestCount++;
        TrackerDiagnostics.AbuseIntelMalformed.Add(1);
        if (!string.IsNullOrWhiteSpace(passkey))
        {
            GetOrCreateScore(_passkeyScores, passkey, now).MalformedRequestCount++;
            GetOrCreateScore(_combinedScores, $"{ip}|{passkey}", now).MalformedRequestCount++;
        }
        EmitEvent(ip, passkey, AbuseEventTypes.MalformedRequest, 3, now);
    }

    public void RecordDeniedPolicy(string ip, string? passkey)
    {
        var now = DateTimeOffset.UtcNow;
        GetOrCreateScore(_ipScores, ip, now).DeniedPolicyCount++;
        TrackerDiagnostics.AbuseIntelDenied.Add(1);
        if (!string.IsNullOrWhiteSpace(passkey))
        {
            GetOrCreateScore(_passkeyScores, passkey, now).DeniedPolicyCount++;
            GetOrCreateScore(_combinedScores, $"{ip}|{passkey}", now).DeniedPolicyCount++;
        }
        EmitEvent(ip, passkey, AbuseEventTypes.DeniedPolicy, 2, now);
    }

    public void RecordPeerIdAnomaly(string ip, string? passkey)
    {
        var now = DateTimeOffset.UtcNow;
        GetOrCreateScore(_ipScores, ip, now).PeerIdAnomalyCount++;
        TrackerDiagnostics.AbuseIntelPeerIdAnomaly.Add(1);
        if (!string.IsNullOrWhiteSpace(passkey))
        {
            GetOrCreateScore(_passkeyScores, passkey, now).PeerIdAnomalyCount++;
        }
        EmitEvent(ip, passkey, AbuseEventTypes.PeerIdAnomaly, 4, now);
    }

    public void RecordSuspiciousPattern(string ip, string? passkey)
    {
        var now = DateTimeOffset.UtcNow;
        GetOrCreateScore(_ipScores, ip, now).SuspiciousPatternCount++;
        TrackerDiagnostics.AbuseIntelSuspicious.Add(1);
        if (!string.IsNullOrWhiteSpace(passkey))
        {
            GetOrCreateScore(_passkeyScores, passkey, now).SuspiciousPatternCount++;
        }
        EmitEvent(ip, passkey, AbuseEventTypes.SuspiciousPattern, 5, now);
    }

    public void RecordScrapeAmplification(string ip)
    {
        var now = DateTimeOffset.UtcNow;
        GetOrCreateScore(_ipScores, ip, now).ScrapeAmplificationCount++;
        TrackerDiagnostics.AbuseIntelScrapeAmplification.Add(1);
        EmitEvent(ip, null, AbuseEventTypes.ScrapeAmplification, 3, now);
    }

    private void EmitEvent(string ip, string? passkey, string eventType, int scoreContribution, DateTimeOffset now)
    {
        _eventWriter?.TryWrite(new AbuseEvent(
            Guid.NewGuid(), _nodeId, ip, passkey, eventType, scoreContribution, null, now));
    }

    public AbuseRestrictionLevel EvaluateIp(string ip)
    {
        MaybeSweep();
        return _ipScores.TryGetValue(ip, out var score) ? score.RestrictionLevel : AbuseRestrictionLevel.None;
    }

    public AbuseRestrictionLevel EvaluatePasskey(string passkey)
    {
        return _passkeyScores.TryGetValue(passkey, out var score) ? score.RestrictionLevel : AbuseRestrictionLevel.None;
    }

    public AbuseRestrictionLevel EvaluateCombined(string ip, string passkey)
    {
        var key = $"{ip}|{passkey}";
        return _combinedScores.TryGetValue(key, out var score) ? score.RestrictionLevel : AbuseRestrictionLevel.None;
    }

    public IReadOnlyList<AbuseDiagnosticsEntry> GetDiagnostics(int maxEntries = 100)
    {
        var entries = new List<AbuseDiagnosticsEntry>();

        foreach (var (key, score) in _ipScores)
        {
            if (score.TotalScore > 0)
            {
                entries.Add(new AbuseDiagnosticsEntry(key, "ip",
                    score.MalformedRequestCount, score.DeniedPolicyCount,
                    score.PeerIdAnomalyCount, score.SuspiciousPatternCount,
                    score.ScrapeAmplificationCount, score.TotalScore,
                    score.RestrictionLevel.ToString(), score.FirstSeenUtc, score.LastSeenUtc));
            }
        }

        foreach (var (key, score) in _passkeyScores)
        {
            if (score.TotalScore > 0)
            {
                entries.Add(new AbuseDiagnosticsEntry(key, "passkey",
                    score.MalformedRequestCount, score.DeniedPolicyCount,
                    score.PeerIdAnomalyCount, score.SuspiciousPatternCount,
                    score.ScrapeAmplificationCount, score.TotalScore,
                    score.RestrictionLevel.ToString(), score.FirstSeenUtc, score.LastSeenUtc));
            }
        }

        return entries
            .OrderByDescending(static e => e.TotalScore)
            .Take(maxEntries)
            .ToList();
    }

    public AbuseDiagnosticsSummary GetSummary()
    {
        var ipCount = 0;
        var passkeyCount = 0;
        var warned = 0;
        var softRestricted = 0;
        var hardBlocked = 0;

        foreach (var (_, score) in _ipScores)
        {
            if (score.TotalScore <= 0) continue;
            ipCount++;
            switch (score.RestrictionLevel)
            {
                case AbuseRestrictionLevel.Warned: warned++; break;
                case AbuseRestrictionLevel.SoftRestrict: softRestricted++; break;
                case AbuseRestrictionLevel.HardBlock: hardBlocked++; break;
            }
        }

        foreach (var (_, score) in _passkeyScores)
        {
            if (score.TotalScore > 0) passkeyCount++;
        }

        return new AbuseDiagnosticsSummary(ipCount, passkeyCount, warned, softRestricted, hardBlocked);
    }

    private void MaybeSweep()
    {
        var nowWindow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (nowWindow - Volatile.Read(ref _lastSweepWindow) < 300) return;
        Volatile.Write(ref _lastSweepWindow, nowWindow);

        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-30);
        SweepDictionary(_ipScores, cutoff);
        SweepDictionary(_passkeyScores, cutoff);
        SweepDictionary(_combinedScores, cutoff);
    }

    private static void SweepDictionary(ConcurrentDictionary<string, AbuseScore> dict, DateTimeOffset cutoff)
    {
        foreach (var (key, score) in dict)
        {
            if (score.LastSeenUtc < cutoff)
            {
                dict.TryRemove(key, out _);
            }
        }
    }

    private static AbuseScore GetOrCreateScore(ConcurrentDictionary<string, AbuseScore> dict, string key, DateTimeOffset now)
    {
        var score = dict.GetOrAdd(key, static _ => new AbuseScore());
        if (score.FirstSeenUtc == default) score.FirstSeenUtc = now;
        score.LastSeenUtc = now;
        return score;
    }
}

public sealed record AbuseDiagnosticsSummary(
    int TrackedIps,
    int TrackedPasskeys,
    int WarnedCount,
    int SoftRestrictedCount,
    int HardBlockedCount);

// ─── Abuse Event Channel + Persistence ──────────────────────────────────────

/// <summary>
/// Bounded channel writer for abuse events. Fire-and-forget from hot path.
/// </summary>
public sealed class AbuseEventChannelWriter : IAbuseEventChannelWriter
{
    private readonly Channel<AbuseEvent> _channel = Channel.CreateBounded<AbuseEvent>(new BoundedChannelOptions(8_192)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropOldest
    });

    internal ChannelReader<AbuseEvent> Reader => _channel.Reader;

    public bool TryWrite(AbuseEvent abuseEvent) => _channel.Writer.TryWrite(abuseEvent);
}

/// <summary>
/// Background service that drains the abuse event channel and persists events to PostgreSQL in batches.
/// Also publishes events to Redis for cross-node visibility.
/// </summary>
public sealed class AbuseEventPersistenceService(
    AbuseEventChannelWriter channelWriter,
    IPostgresConnectionFactory postgresConnectionFactory,
    IRedisCacheClient redisCacheClient,
    ILogger<AbuseEventPersistenceService> logger) : BackgroundService
{
    private const string InsertSql =
        "INSERT INTO abuse_events (id, node_id, ip, passkey, event_type, score_contribution, detail, occurred_at_utc) " +
        "VALUES ($1, $2, $3, $4, $5, $6, $7, $8) ON CONFLICT DO NOTHING";

    private const string AbuseRedisChannel = "tracker:abuse:events";
    private const int MaxBatchSize = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnsureTableAsync(stoppingToken);

        var batch = new List<AbuseEvent>(MaxBatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Wait for first item
                if (!await channelWriter.Reader.WaitToReadAsync(stoppingToken))
                    break;

                // Drain available items up to batch size
                batch.Clear();
                while (batch.Count < MaxBatchSize && channelWriter.Reader.TryRead(out var item))
                {
                    batch.Add(item);
                }

                if (batch.Count == 0)
                    continue;

                // Persist batch to PostgreSQL
                await PersistBatchAsync(batch, stoppingToken);

                // Publish to Redis for cross-node aggregation (best effort)
                await PublishBatchAsync(batch);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Abuse event persistence batch failed. {Count} events dropped.", batch.Count);
            }
        }
    }

    private async Task EnsureTableAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await postgresConnectionFactory.OpenConnectionAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand("""
                CREATE TABLE IF NOT EXISTS abuse_events (
                    id uuid PRIMARY KEY,
                    node_id text NOT NULL,
                    ip text NOT NULL,
                    passkey text,
                    event_type text NOT NULL,
                    score_contribution integer NOT NULL,
                    detail text,
                    occurred_at_utc timestamp with time zone NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_abuse_events_occurred ON abuse_events (occurred_at_utc DESC);
                CREATE INDEX IF NOT EXISTS ix_abuse_events_ip ON abuse_events (ip, occurred_at_utc DESC);
                """, connection);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            logger.LogInformation("Abuse events table ensured.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to ensure abuse_events table. Persistence may fail.");
        }
    }

    private async Task PersistBatchAsync(List<AbuseEvent> batch, CancellationToken cancellationToken)
    {
        await using var connection = await postgresConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var batchCommand = new NpgsqlBatch(connection);

        foreach (var evt in batch)
        {
            var cmd = new NpgsqlBatchCommand(InsertSql);
            cmd.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = evt.Id });
            cmd.Parameters.Add(new NpgsqlParameter<string> { TypedValue = evt.NodeId });
            cmd.Parameters.Add(new NpgsqlParameter<string> { TypedValue = evt.Ip });
            cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)evt.Passkey ?? DBNull.Value });
            cmd.Parameters.Add(new NpgsqlParameter<string> { TypedValue = evt.EventType });
            cmd.Parameters.Add(new NpgsqlParameter<int> { TypedValue = evt.ScoreContribution });
            cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)evt.Detail ?? DBNull.Value });
            cmd.Parameters.Add(new NpgsqlParameter<DateTime> { TypedValue = evt.OccurredAtUtc.UtcDateTime });
            batchCommand.BatchCommands.Add(cmd);
        }

        await batchCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task PublishBatchAsync(List<AbuseEvent> batch)
    {
        try
        {
            foreach (var evt in batch)
            {
                var json = JsonSerializer.Serialize(evt, JsonOptions);
                await redisCacheClient.Subscriber.PublishAsync(
                    StackExchange.Redis.RedisChannel.Literal(AbuseRedisChannel), json);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to publish abuse events to Redis. Events are still persisted.");
        }
    }
}

/// <summary>
/// Startup configuration validator. Checks for dangerous or incompatible settings.
/// </summary>
public static class StartupConfigurationValidator
{
    public sealed record ConfigValidationResult(bool IsValid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

    public static ConfigValidationResult Validate(
        TrackerSecurityOptions security,
        TrackerCompatibilityOptions compatibility,
        TrackerGovernanceOptions governance,
        TrackerAbuseProtectionOptions abuseProtection,
        GatewayRuntimeOptions runtime)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // Incompatible compact settings
        if (!security.RequireCompactResponses && compatibility.CompatibilityMode == ClientCompatibilityMode.Strict)
        {
            warnings.Add("Strict compatibility mode with RequireCompactResponses=false may cause unexpected behavior with strict clients.");
        }

        // Unsafe scrape exposure
        if (security.AllowPasskeyInQueryString)
        {
            warnings.Add("AllowPasskeyInQueryString=true exposes passkeys in URLs and proxy logs. Consider using path-based passkey routing.");
        }

        // Permissive mode without abuse protection
        if (compatibility.StrictnessProfile == ProtocolStrictnessProfile.Permissive &&
            !abuseProtection.EnableAnnounceIpRateLimit && !abuseProtection.EnableAnnouncePasskeyRateLimit)
        {
            warnings.Add("Permissive strictness with no rate limiting enabled is unsafe for production.");
        }

        // IPv6 without compact
        if (security.AllowIPv6Peers && !security.RequireCompactResponses)
        {
            warnings.Add("IPv6 peers with non-compact responses may cause compatibility issues with older clients.");
        }

        // Global maintenance with announce enabled
        if (governance.GlobalMaintenanceMode && !governance.AnnounceDisabled)
        {
            warnings.Add("GlobalMaintenanceMode is set but AnnounceDisabled is false. Maintenance mode will override announce availability.");
        }

        // Emergency abuse mitigation with permissive profile
        if (governance.EmergencyAbuseMitigation && compatibility.StrictnessProfile == ProtocolStrictnessProfile.Permissive)
        {
            warnings.Add("EmergencyAbuseMitigation with Permissive strictness reduces effectiveness of abuse mitigation.");
        }

        // Peer TTL too low
        if (runtime.PeerTtlSeconds < 300)
        {
            warnings.Add($"PeerTtlSeconds={runtime.PeerTtlSeconds} is very aggressive. Peers may be expired before re-announcing.");
        }

        // Hard max numwant vs max peers per response mismatch
        if (security.HardMaxNumWant > runtime.MaxPeersPerResponse * 3)
        {
            warnings.Add($"HardMaxNumWant={security.HardMaxNumWant} is much larger than MaxPeersPerResponse={runtime.MaxPeersPerResponse}. Clients may request more peers than will be returned.");
        }

        // Private/public policy overlap warning
        if (security.AllowPasskeyInQueryString && !security.RequireCompactResponses)
        {
            warnings.Add("Combination of passkey-in-querystring and non-compact responses creates risk of passkey leakage in non-compact peer dictionaries.");
        }

        // Client IP override is a security-sensitive feature
        if (security.AllowClientIpOverride)
        {
            warnings.Add("AllowClientIpOverride=true allows clients to specify their own IP address via the 'ip' query parameter. This is a security risk unless the tracker operates behind a trusted proxy that does not preserve client IPs.");
        }

        return new ConfigValidationResult(errors.Count == 0, errors, warnings);
    }
}
