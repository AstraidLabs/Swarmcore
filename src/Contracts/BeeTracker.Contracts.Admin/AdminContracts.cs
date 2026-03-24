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

public sealed record UserPermissionAdminDto(
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
    IReadOnlyCollection<BulkBanOperationItemDto> BanItems);

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

public sealed record BulkUserPermissionOperationItemDto(
    Guid UserId,
    bool Succeeded,
    string? ErrorCode,
    string? ErrorMessage,
    UserPermissionAdminDto? Snapshot);

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

// ─── Governance Diagnostics ─────────────────────────────────────────────────

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
