namespace BeeTracker.Contracts.Admin;

public sealed record NodeHealthDto(string NodeId, string Region, bool Ready, DateTimeOffset ObservedAtUtc);

// ─── Cluster / Distributed Runtime ───────────────────────────────────────────

/// <summary>Ownership summary for a single cluster shard, returned by admin diagnostics.</summary>
public sealed record ClusterShardOwnershipDto(
    int ShardId,
    string? OwnerNodeId,
    bool LocallyOwned,
    DateTimeOffset? LeaseExpiresAtUtc);

/// <summary>Cluster-level view of a single node returned by admin diagnostics.</summary>
public sealed record ClusterNodeStateDto(
    string NodeId,
    string Region,
    string OperationalState,
    int OwnedShardCount,
    DateTimeOffset HeartbeatObservedAtUtc,
    bool HeartbeatFresh);

/// <summary>Full cluster shard ownership map returned by admin diagnostics.</summary>
public sealed record ClusterShardDiagnosticsDto(
    DateTimeOffset ObservedAtUtc,
    int TotalShards,
    int OwnedShards,
    int UnownedShards,
    IReadOnlyCollection<ClusterShardOwnershipDto> Shards);

/// <summary>Request body for node drain / maintenance state transitions.</summary>
public sealed record NodeStateTransitionRequest(
    string TargetState,
    bool Force = false);

public sealed record CacheStatusDto(string Layer, string Scope, bool Healthy);

public sealed record TrackerOverviewDto(
    string NodeId,
    int ActiveSwarms,
    int ActivePeers,
    long TelemetryQueueLength,
    IReadOnlyCollection<CacheStatusDto> CacheLayers);

public sealed record ClusterOverviewDto(
    DateTimeOffset ObservedAtUtc,
    int ActiveNodeCount,
    IReadOnlyCollection<NodeHealthDto> Nodes);

public sealed record AuditRecordDto(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    string ActorId,
    string ActorRole,
    string Action,
    string Severity,
    string EntityType,
    string EntityId,
    string CorrelationId,
    string? RequestId,
    string Result,
    string? IpAddress);

public sealed record MaintenanceRunDto(
    Guid Id,
    string Operation,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    string Status,
    string CorrelationId);

public sealed record TorrentAdminDto(
    string InfoHash,
    bool IsPrivate,
    bool IsEnabled,
    int AnnounceIntervalSeconds,
    int MinAnnounceIntervalSeconds,
    int DefaultNumWant,
    int MaxNumWant,
    bool AllowScrape,
    long Version,
    bool CompactOnly = true,
    bool AllowUdp = true,
    bool AllowIPv6 = true,
    int? StrictnessProfileOverride = null,
    int? CompatibilityModeOverride = null,
    string? ModerationState = null,
    bool MaintenanceFlag = false,
    bool TemporaryRestriction = false);

public sealed record PasskeyAdminDto(
    string PasskeyMask,
    Guid UserId,
    bool IsRevoked,
    DateTimeOffset? ExpiresAtUtc,
    long Version);

#pragma warning disable CS0618
[Obsolete("Use TrackerAccessAdminDto. UserPermissionAdminDto is a compatibility alias and will be removed in a future release.")]
public sealed record UserPermissionAdminDto(
    Guid UserId,
    bool CanLeech,
    bool CanSeed,
    bool CanScrape,
    bool CanUsePrivateTracker,
    long Version);

public sealed record TrackerAccessAdminDto(
    Guid UserId,
    bool CanLeech,
    bool CanSeed,
    bool CanScrape,
    bool CanUsePrivateTracker,
    long Version);

public sealed record BanRuleAdminDto(
    string Scope,
    string Subject,
    string Reason,
    DateTimeOffset? ExpiresAtUtc,
    long Version);

public sealed record BulkOperationResultDto(
    int TotalCount,
    int SucceededCount,
    int FailedCount,
    IReadOnlyCollection<BulkPasskeyOperationItemDto> PasskeyItems,
    IReadOnlyCollection<BulkTorrentOperationItemDto> TorrentItems,
    IReadOnlyCollection<BulkUserPermissionOperationItemDto> PermissionItems,
    IReadOnlyCollection<BulkBanOperationItemDto> BanItems,
    IReadOnlyCollection<BulkTrackerAccessOperationItemDto>? TrackerAccessItems = null);

public sealed record BulkDryRunResultDto(
    int TotalCount,
    int ApplicableCount,
    int RejectedCount,
    IReadOnlyCollection<TorrentPolicyDryRunItemDto> TorrentPolicyItems);

public sealed record BulkPasskeyOperationItemDto(
    string PasskeyMask,
    bool Succeeded,
    string? ErrorCode,
    string? ErrorMessage,
    PasskeyAdminDto? Snapshot,
    string? NewPasskey,
    string? NewPasskeyMask);

[Obsolete("Use BulkTrackerAccessOperationItemDto. BulkUserPermissionOperationItemDto is a compatibility alias and will be removed in a future release.")]
public sealed record BulkUserPermissionOperationItemDto(
    Guid UserId,
    bool Succeeded,
    string? ErrorCode,
    string? ErrorMessage,
    UserPermissionAdminDto? Snapshot);

public sealed record BulkTrackerAccessOperationItemDto(
    Guid UserId,
    bool Succeeded,
    string? ErrorCode,
    string? ErrorMessage,
    TrackerAccessAdminDto? Snapshot);

public sealed record BulkTorrentOperationItemDto(
    string InfoHash,
    bool Succeeded,
    string? ErrorCode,
    string? ErrorMessage,
    TorrentAdminDto? Snapshot);

public sealed record TorrentPolicyDryRunItemDto(
    string InfoHash,
    bool CanApply,
    string? ErrorCode,
    string? ErrorMessage,
    TorrentAdminDto? CurrentSnapshot,
    TorrentAdminDto ProposedSnapshot,
    IReadOnlyCollection<string> Warnings);

public sealed record BulkBanOperationItemDto(
    string Scope,
    string Subject,
    bool Succeeded,
    string? ErrorCode,
    string? ErrorMessage,
    BanRuleAdminDto? Snapshot);

public static class TrackerAccessAdminContractConversions
{
    public static TrackerAccessAdminDto ToTrackerAccessAdminDto(this UserPermissionAdminDto permission)
        => new(
            permission.UserId,
            permission.CanLeech,
            permission.CanSeed,
            permission.CanScrape,
            permission.CanUsePrivateTracker,
            permission.Version);

    public static BulkTrackerAccessOperationItemDto ToTrackerAccessOperationItem(this BulkUserPermissionOperationItemDto item)
        => new(
            item.UserId,
            item.Succeeded,
            item.ErrorCode,
            item.ErrorMessage,
            item.Snapshot?.ToTrackerAccessAdminDto());

    public static UserPermissionAdminDto ToUserPermissionAdminDto(this TrackerAccessAdminDto permission)
        => new(
            permission.UserId,
            permission.CanLeech,
            permission.CanSeed,
            permission.CanScrape,
            permission.CanUsePrivateTracker,
            permission.Version);

    public static BulkUserPermissionOperationItemDto ToUserPermissionOperationItem(this BulkTrackerAccessOperationItemDto item)
        => new(
            item.UserId,
            item.Succeeded,
            item.ErrorCode,
            item.ErrorMessage,
            item.Snapshot?.ToUserPermissionAdminDto());
}

// ─── Governance Diagnostics ─────────────────────────────────────────────────

#pragma warning restore CS0618
public sealed record GovernanceStateDto(
    bool AnnounceDisabled,
    bool ScrapeDisabled,
    bool GlobalMaintenanceMode,
    bool ReadOnlyMode,
    bool EmergencyAbuseMitigation,
    bool UdpDisabled,
    bool IPv6Frozen,
    bool PolicyFreezeMode,
    string CompatibilityMode,
    string StrictnessProfile);

public sealed record GovernanceUpdateRequest(
    bool? AnnounceDisabled = null,
    bool? ScrapeDisabled = null,
    bool? GlobalMaintenanceMode = null,
    bool? ReadOnlyMode = null,
    bool? EmergencyAbuseMitigation = null,
    bool? UdpDisabled = null,
    bool? IPv6Frozen = null,
    bool? PolicyFreezeMode = null,
    string? CompatibilityMode = null,
    string? StrictnessProfile = null);

public sealed record AbuseDiagnosticsDto(
    int TrackedIps,
    int TrackedPasskeys,
    int WarnedCount,
    int SoftRestrictedCount,
    int HardBlockedCount,
    IReadOnlyCollection<AbuseDiagnosticsEntryDto> TopOffenders);

public sealed record AbuseDiagnosticsEntryDto(
    string Key,
    string KeyType,
    int MalformedRequestCount,
    int DeniedPolicyCount,
    int PeerIdAnomalyCount,
    int SuspiciousPatternCount,
    int ScrapeAmplificationCount,
    int TotalScore,
    string RestrictionLevel);

public sealed record ConfigValidationDto(
    bool IsValid,
    IReadOnlyCollection<string> Errors,
    IReadOnlyCollection<string> Warnings);

public sealed record RuntimeDiagnosticsDto(
    GovernanceStateDto Governance,
    AbuseDiagnosticsDto AbuseSummary,
    ConfigValidationDto ConfigValidation,
    TrackerOverviewDto TrackerOverview);

public sealed record TrackerNodeDependencySummaryDto(
    bool Enabled,
    string Summary,
    bool Healthy);

public sealed record TrackerNodeOverviewDto(
    string NodeKey,
    long Version,
    string NodeId,
    string NodeName,
    string Environment,
    string Region,
    string PublicBaseUrl,
    string InternalBaseUrl,
    bool HttpEnabled,
    bool ScrapeEnabled,
    bool UdpEnabled,
    bool PublicTrackerEnabled,
    bool PrivateTrackerEnabled,
    bool IPv6Enabled,
    int ShardCount,
    bool CompactResponsesByDefault,
    bool AllowNonCompactResponses,
    bool RequiresRestart,
    string ApplyMode,
    DateTimeOffset UpdatedAtUtc,
    string UpdatedBy);

public sealed record TrackerNodeCapabilitiesDto(
    bool SupportsHttp,
    bool SupportsUdp,
    bool SupportsPublicTracker,
    bool SupportsPrivateTracker,
    bool SupportsPasskeyRouting,
    bool SupportsClientIpOverride,
    bool SupportsNonCompactResponses,
    bool SupportsIPv6Peers,
    bool SupportsAuditPersistence,
    bool SupportsTelemetryPersistence,
    bool SupportsDiagnosticsEndpoints);

public sealed record TrackerNodeProtocolViewDto(
    string AnnounceRoute,
    string PrivateAnnounceRoute,
    string ScrapeRoute,
    bool HttpAnnounceEnabled,
    bool HttpScrapeEnabled,
    bool UdpEnabled,
    string UdpBindAddress,
    int UdpPort,
    bool UdpScrapeEnabled,
    int DefaultAnnounceIntervalSeconds,
    int MinAnnounceIntervalSeconds,
    int DefaultNumWant,
    int MaxNumWant,
    bool CompactResponsesByDefault,
    bool AllowNonCompactResponses,
    bool AllowPasskeyInPath,
    bool AllowPasskeyInQuery,
    bool AllowClientIpOverride);

public sealed record TrackerNodeRuntimeViewDto(
    int ShardCount,
    int PeerTtlSeconds,
    int CleanupIntervalSeconds,
    int MaxPeersPerResponse,
    int? MaxPeersPerSwarm,
    bool PreferLocalShardPeers,
    bool EnableCompletedAccounting,
    bool EnableIPv6Peers,
    bool AnnounceDisabled,
    bool ScrapeDisabled,
    bool UdpDisabled,
    bool GlobalMaintenanceMode,
    bool ReadOnlyMode);

public sealed record TrackerNodeCoordinationViewDto(
    TrackerNodeDependencySummaryDto Redis,
    TrackerNodeDependencySummaryDto Postgres,
    string KeyPrefix,
    string InvalidationChannel,
    bool MigrateOnStart,
    bool PersistTelemetry,
    bool PersistAudit,
    int TelemetryBatchSize,
    int TelemetryFlushIntervalMilliseconds,
    int HeartbeatTtlSeconds,
    int OwnershipLeaseDurationSeconds,
    int OwnershipRefreshIntervalSeconds,
    int SwarmSummaryPublishIntervalSeconds,
    int SwarmSummaryTtlSeconds);

public sealed record TrackerNodePolicyViewDto(
    bool EnablePublicTracker,
    bool EnablePrivateTracker,
    bool RequirePasskeyForPrivateTracker,
    bool AllowPublicScrape,
    bool AllowPrivateScrape,
    string DefaultTorrentVisibility,
    string StrictnessProfile,
    string CompatibilityMode,
    int HardMaxNumWant,
    bool EmergencyAbuseMitigation,
    bool PolicyFreezeMode,
    bool IPv6Frozen);

public sealed record TrackerNodeObservabilityViewDto(
    bool EnableHealthEndpoints,
    bool EnableMetrics,
    bool EnableTracing,
    bool EnableDiagnosticsEndpoints,
    string LiveRoute,
    string ReadyRoute,
    string StartupRoute,
    bool RequireRedisForReadiness,
    bool RequirePostgresForReadiness);

public sealed record TrackerNodeAbuseViewDto(
    int MaxAnnounceQueryLength,
    int MaxScrapeQueryLength,
    int MaxQueryParameterCount,
    int HardMaxNumWant,
    bool EnableAnnouncePasskeyRateLimit,
    int AnnouncePerPasskeyPerSecond,
    bool EnableAnnounceIpRateLimit,
    int AnnouncePerIpPerSecond,
    bool EnableScrapeIpRateLimit,
    int ScrapePerIpPerSecond,
    bool RejectOversizedRequests,
    int MaxScrapeInfoHashes);

public sealed record TrackerNodeConfigViewDto(
    TrackerNodeOverviewDto Overview,
    TrackerNodeCapabilitiesDto Capabilities,
    TrackerNodeProtocolViewDto Protocol,
    TrackerNodeRuntimeViewDto Runtime,
    TrackerNodeCoordinationViewDto Coordination,
    TrackerNodePolicyViewDto Policy,
    TrackerNodeObservabilityViewDto Observability,
    TrackerNodeAbuseViewDto Abuse,
    ConfigValidationDto Validation);
