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

    // ─── Governance ──────────────────────────────────────────────────────────
    public static readonly Counter<long> GovernanceAnnounceRejected = Meter.CreateCounter<long>("tracker.governance.announce_rejected");
    public static readonly Counter<long> GovernanceScrapeRejected = Meter.CreateCounter<long>("tracker.governance.scrape_rejected");
    public static readonly Counter<long> GovernanceMaintenanceRejected = Meter.CreateCounter<long>("tracker.governance.maintenance_rejected");
    public static readonly Counter<long> GovernanceReadOnlySkipped = Meter.CreateCounter<long>("tracker.governance.readonly_skipped");
    public static readonly Counter<long> GovernanceUdpRejected = Meter.CreateCounter<long>("tracker.governance.udp_rejected");
    public static readonly Counter<long> GovernanceStateChanges = Meter.CreateCounter<long>("tracker.governance.state_changes");

    // ─── Compatibility ───────────────────────────────────────────────────────
    public static readonly Counter<long> CompatibilityFallback = Meter.CreateCounter<long>("tracker.compatibility.fallback");
    public static readonly Counter<long> CompatibilityWarningIssued = Meter.CreateCounter<long>("tracker.compatibility.warning_issued");
    public static readonly Counter<long> StrictnessRejected = Meter.CreateCounter<long>("tracker.strictness.rejected");
    public static readonly Counter<long> StrictnessClamped = Meter.CreateCounter<long>("tracker.strictness.clamped");

    // ─── Advanced Abuse Intelligence ─────────────────────────────────────────
    public static readonly Counter<long> AbuseIntelMalformed = Meter.CreateCounter<long>("tracker.abuse_intel.malformed");
    public static readonly Counter<long> AbuseIntelDenied = Meter.CreateCounter<long>("tracker.abuse_intel.denied");
    public static readonly Counter<long> AbuseIntelPeerIdAnomaly = Meter.CreateCounter<long>("tracker.abuse_intel.peer_id_anomaly");
    public static readonly Counter<long> AbuseIntelSuspicious = Meter.CreateCounter<long>("tracker.abuse_intel.suspicious");
    public static readonly Counter<long> AbuseIntelScrapeAmplification = Meter.CreateCounter<long>("tracker.abuse_intel.scrape_amplification");
    public static readonly Counter<long> AbuseIntelSoftRestrict = Meter.CreateCounter<long>("tracker.abuse_intel.soft_restrict");
    public static readonly Counter<long> AbuseIntelHardBlock = Meter.CreateCounter<long>("tracker.abuse_intel.hard_block");

    // ─── Per-Torrent Override Usage ──────────────────────────────────────────
    public static readonly Counter<long> TorrentOverrideApplied = Meter.CreateCounter<long>("tracker.torrent_override.applied");
    public static readonly Counter<long> TorrentMaintenanceRejected = Meter.CreateCounter<long>("tracker.torrent.maintenance_rejected");
    public static readonly Counter<long> TorrentTemporaryRestrictionRejected = Meter.CreateCounter<long>("tracker.torrent.temporary_restriction_rejected");

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
