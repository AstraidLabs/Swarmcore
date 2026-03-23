using System.Diagnostics.Metrics;

namespace Swarmcore.BuildingBlocks.Observability.Diagnostics;

public static class TrackerDiagnostics
{
    public const string MeterName = "Swarmcore.Tracker";

    public static readonly Meter Meter = new(MeterName);
    public static readonly Counter<long> TelemetryDropped = Meter.CreateCounter<long>("tracker.telemetry.dropped");

    public static readonly Counter<long> AnnounceTotal = Meter.CreateCounter<long>("tracker.announce.total");
    public static readonly Counter<long> AnnounceDenied = Meter.CreateCounter<long>("tracker.announce.denied");
    public static readonly Counter<long> ScrapeTotal = Meter.CreateCounter<long>("tracker.scrape.total");
    public static readonly Counter<long> ScrapeDenied = Meter.CreateCounter<long>("tracker.scrape.denied");
    public static readonly Counter<long> UdpConnectTotal = Meter.CreateCounter<long>("tracker.udp.connect.total");
    public static readonly Counter<long> UdpAnnounceTotal = Meter.CreateCounter<long>("tracker.udp.announce.total");
    public static readonly Counter<long> UdpScrapeTotal = Meter.CreateCounter<long>("tracker.udp.scrape.total");
    public static readonly Counter<long> UdpErrorTotal = Meter.CreateCounter<long>("tracker.udp.error.total");
    public static readonly Counter<long> CacheHit = Meter.CreateCounter<long>("tracker.cache.hit");
    public static readonly Counter<long> CacheMiss = Meter.CreateCounter<long>("tracker.cache.miss");
    public static readonly Counter<long> AbuseThrottled = Meter.CreateCounter<long>("tracker.abuse.throttled");
    public static readonly Counter<long> RequestMalformed = Meter.CreateCounter<long>("tracker.request.malformed");
    public static readonly Counter<long> RequestParseFailed = Meter.CreateCounter<long>("tracker.request.parse_failed");
    public static readonly Counter<long> RequestValidationFailed = Meter.CreateCounter<long>("tracker.request.validation_failed");
    public static readonly Histogram<double> AnnounceDuration = Meter.CreateHistogram<double>("tracker.announce.duration", unit: "ms");
    public static readonly Histogram<double> ScrapeDuration = Meter.CreateHistogram<double>("tracker.scrape.duration", unit: "ms");

    // ─── Cluster / Distributed Runtime ───────────────────────────────────────

    /// <summary>Number of ownership transitions observed (any shard changing owner).</summary>
    public static readonly Counter<long> ClusterOwnershipTransitions = Meter.CreateCounter<long>("tracker.cluster.ownership_transitions");

    /// <summary>Number of successful shard ownership claims (new claim or reclaim after failover).</summary>
    public static readonly Counter<long> ClusterOwnershipClaims = Meter.CreateCounter<long>("tracker.cluster.ownership_claims");

    /// <summary>Number of failed shard ownership claim attempts (e.g., Redis unavailable or conflict).</summary>
    public static readonly Counter<long> ClusterOwnershipClaimFailures = Meter.CreateCounter<long>("tracker.cluster.ownership_claim_failures");

    /// <summary>Number of failover events where a node's expired shards were reclaimed by another node.</summary>
    public static readonly Counter<long> ClusterFailoverEvents = Meter.CreateCounter<long>("tracker.cluster.failover");

    /// <summary>Number of times this node released ownership (e.g., during drain or shutdown).</summary>
    public static readonly Counter<long> ClusterOwnershipReleases = Meter.CreateCounter<long>("tracker.cluster.ownership_releases");

    /// <summary>Number of swarm summary batches published to the distributed cache.</summary>
    public static readonly Counter<long> ClusterSwarmSummariesPublished = Meter.CreateCounter<long>("tracker.cluster.swarm_summaries_published");

    /// <summary>Number of node state transitions (Active → Draining → Maintenance etc.).</summary>
    public static readonly Counter<long> ClusterNodeStateChanges = Meter.CreateCounter<long>("tracker.cluster.node_state_changes");

    /// <summary>Number of announce/scrape requests served for locally-owned vs non-locally-owned swarms.</summary>
    public static readonly Counter<long> ClusterRequestLocality = Meter.CreateCounter<long>("tracker.cluster.request_locality");

    /// <summary>Number of ownership lease refresh failures (node failed to extend its lease).</summary>
    public static readonly Counter<long> ClusterOwnershipRefreshFailures = Meter.CreateCounter<long>("tracker.cluster.ownership_refresh_failures");

    // ─── Security & Compliance ───────────────────────────────────────────────

    /// <summary>Number of passkey denials due to revocation.</summary>
    public static readonly Counter<long> PasskeyDeniedRevoked = Meter.CreateCounter<long>("tracker.security.passkey_denied_revoked");

    /// <summary>Number of passkey denials due to expiration.</summary>
    public static readonly Counter<long> PasskeyDeniedExpired = Meter.CreateCounter<long>("tracker.security.passkey_denied_expired");

    /// <summary>Number of passkey rotation events.</summary>
    public static readonly Counter<long> PasskeyRotated = Meter.CreateCounter<long>("tracker.security.passkey_rotated");

    /// <summary>Number of passkey revocation events.</summary>
    public static readonly Counter<long> PasskeyRevoked = Meter.CreateCounter<long>("tracker.security.passkey_revoked");

    /// <summary>Number of unauthorized admin access attempts.</summary>
    public static readonly Counter<long> AdminAccessDenied = Meter.CreateCounter<long>("tracker.security.admin_access_denied");

    /// <summary>Number of unsafe configuration rejections on startup.</summary>
    public static readonly Counter<long> ConfigValidationRejected = Meter.CreateCounter<long>("tracker.security.config_validation_rejected");

    /// <summary>Number of audit records generated for critical actions.</summary>
    public static readonly Counter<long> AuditRecordsGenerated = Meter.CreateCounter<long>("tracker.audit.records_generated");

    /// <summary>Number of audit records dropped due to buffer overflow.</summary>
    public static readonly Counter<long> AuditRecordsDropped = Meter.CreateCounter<long>("tracker.audit.records_dropped");

    // ─── Resilience & Degraded Mode ──────────────────────────────────────────

    /// <summary>Number of times the system entered degraded mode.</summary>
    public static readonly Counter<long> DegradedModeEntered = Meter.CreateCounter<long>("tracker.resilience.degraded_mode_entered");

    /// <summary>Number of times the system recovered from degraded mode.</summary>
    public static readonly Counter<long> DegradedModeExited = Meter.CreateCounter<long>("tracker.resilience.degraded_mode_exited");

    /// <summary>Number of Redis connection failures detected.</summary>
    public static readonly Counter<long> RedisFailures = Meter.CreateCounter<long>("tracker.resilience.redis_failures");

    /// <summary>Number of PostgreSQL connection failures detected.</summary>
    public static readonly Counter<long> PostgresFailures = Meter.CreateCounter<long>("tracker.resilience.postgres_failures");

    /// <summary>Number of requests served in degraded mode (cache-only).</summary>
    public static readonly Counter<long> DegradedModeRequestsServed = Meter.CreateCounter<long>("tracker.resilience.degraded_mode_requests_served");

    /// <summary>Number of cache warmup operations completed.</summary>
    public static readonly Counter<long> CacheWarmupCompleted = Meter.CreateCounter<long>("tracker.resilience.cache_warmup_completed");

    /// <summary>Number of cache warmup operations that failed or timed out.</summary>
    public static readonly Counter<long> CacheWarmupFailed = Meter.CreateCounter<long>("tracker.resilience.cache_warmup_failed");

    /// <summary>Number of worker recovery events after failure.</summary>
    public static readonly Counter<long> WorkerRecoveryEvents = Meter.CreateCounter<long>("tracker.resilience.worker_recovery");

    /// <summary>Number of partial startup recovery events.</summary>
    public static readonly Counter<long> PartialStartupRecovery = Meter.CreateCounter<long>("tracker.resilience.partial_startup_recovery");

    /// <summary>Number of node rejoin events after restart.</summary>
    public static readonly Counter<long> NodeRejoinEvents = Meter.CreateCounter<long>("tracker.resilience.node_rejoin");

    /// <summary>Number of configuration backup/export operations.</summary>
    public static readonly Counter<long> ConfigExportOperations = Meter.CreateCounter<long>("tracker.resilience.config_export");

    /// <summary>Number of configuration restore/import operations.</summary>
    public static readonly Counter<long> ConfigRestoreOperations = Meter.CreateCounter<long>("tracker.resilience.config_restore");

    public static void RegisterSwarmStoreGauges(Func<long> peersCallback, Func<long> swarmsCallback)
    {
        Meter.CreateObservableGauge("tracker.peers.active", peersCallback);
        Meter.CreateObservableGauge("tracker.swarms.active", swarmsCallback);
    }

    public static void RegisterTelemetryQueueGauge(Func<int> queueLengthCallback)
    {
        Meter.CreateObservableGauge("tracker.telemetry.queue_length", () => queueLengthCallback());
    }

    /// <summary>
    /// Registers observable gauges for cluster ownership state.
    /// ownedShardsCallback: number of cluster shards currently owned by this node.
    /// totalShardsCallback: configured total number of cluster shards.
    /// </summary>
    public static void RegisterClusterOwnershipGauges(Func<int> ownedShardsCallback, Func<int> totalShardsCallback)
    {
        Meter.CreateObservableGauge("tracker.cluster.owned_shards", () => ownedShardsCallback());
        Meter.CreateObservableGauge("tracker.cluster.total_shards", () => totalShardsCallback());
        Meter.CreateObservableGauge("tracker.cluster.unowned_shards", () => totalShardsCallback() - ownedShardsCallback());
    }

    /// <summary>
    /// Registers an observable gauge for node heartbeat age (seconds since last heartbeat per node).
    /// </summary>
    public static void RegisterHeartbeatAgeGauge(Func<IEnumerable<(string NodeId, double AgeSeconds)>> callback)
    {
        Meter.CreateObservableGauge("tracker.cluster.heartbeat_age_seconds",
            () => callback().Select(static x =>
                new Measurement<double>(x.AgeSeconds, new KeyValuePair<string, object?>("node_id", x.NodeId))));
    }
}
