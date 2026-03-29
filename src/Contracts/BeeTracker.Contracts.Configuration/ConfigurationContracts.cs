namespace BeeTracker.Contracts.Configuration;

public enum ConfigurationCacheInvalidationKind : byte
{
    TorrentPolicy = 1,
    Passkey = 2,
    UserPermission = 3,
    BanRule = 4
}

public readonly record struct ConfigurationCacheInvalidationMessage(ConfigurationCacheInvalidationKind Kind, string Key);

public sealed record TorrentPolicyDto(
    string InfoHash,
    bool IsPrivate,
    bool IsEnabled,
    int AnnounceIntervalSeconds,
    int MinAnnounceIntervalSeconds,
    int DefaultNumWant,
    int MaxNumWant,
    bool AllowScrape,
    long Version,
    string? WarningMessage = null,
    bool CompactOnly = true,
    bool AllowUdp = true,
    bool AllowIPv6 = true,
    int? StrictnessProfileOverride = null,
    int? CompatibilityModeOverride = null,
    string? ModerationState = null,
    bool MaintenanceFlag = false,
    bool TemporaryRestriction = false);

public sealed record TorrentPolicyMutationPreviewDto(
    bool CanApply,
    string? ErrorCode,
    string? ErrorMessage,
    TorrentPolicyDto? CurrentSnapshot,
    TorrentPolicyDto ProposedSnapshot,
    IReadOnlyList<string> Warnings);

public sealed record PasskeyAccessDto(
    string Passkey,
    Guid UserId,
    bool IsRevoked,
    DateTimeOffset? ExpiresAtUtc,
    long Version);

[Obsolete("Use TrackerAccessRightsDto. UserPermissionSnapshotDto is a compatibility alias and will be removed in a future release.")]
public sealed record UserPermissionSnapshotDto(
    Guid UserId,
    bool CanLeech,
    bool CanSeed,
    bool CanScrape,
    bool CanUsePrivateTracker,
    long Version);

public sealed record BanRuleDto(
    string Scope,
    string Subject,
    string Reason,
    DateTimeOffset? ExpiresAtUtc,
    long Version);

public sealed record TorrentPolicyUpsertRequest(
    bool IsPrivate,
    bool IsEnabled,
    int AnnounceIntervalSeconds,
    int MinAnnounceIntervalSeconds,
    int DefaultNumWant,
    int MaxNumWant,
    bool AllowScrape,
    string? WarningMessage = null,
    long? ExpectedVersion = null,
    bool CompactOnly = true,
    bool AllowUdp = true,
    bool AllowIPv6 = true,
    int? StrictnessProfileOverride = null,
    int? CompatibilityModeOverride = null,
    string? ModerationState = null,
    bool MaintenanceFlag = false,
    bool TemporaryRestriction = false);

public sealed record TorrentActivationRequest(
    long? ExpectedVersion = null);

public sealed record BulkTorrentActivationItem(
    string InfoHash,
    long? ExpectedVersion = null);

public sealed record BulkTorrentPolicyUpsertItem(
    string InfoHash,
    bool IsPrivate,
    bool IsEnabled,
    int AnnounceIntervalSeconds,
    int MinAnnounceIntervalSeconds,
    int DefaultNumWant,
    int MaxNumWant,
    bool AllowScrape,
    string? WarningMessage = null,
    long? ExpectedVersion = null,
    bool CompactOnly = true,
    bool AllowUdp = true,
    bool AllowIPv6 = true,
    int? StrictnessProfileOverride = null,
    int? CompatibilityModeOverride = null,
    string? ModerationState = null,
    bool MaintenanceFlag = false,
    bool TemporaryRestriction = false);

public sealed record PasskeyUpsertRequest(
    Guid UserId,
    bool IsRevoked,
    DateTimeOffset? ExpiresAtUtc,
    long? ExpectedVersion = null);

public sealed record PasskeyRevokeRequest(
    long? ExpectedVersion = null);

public sealed record PasskeyRotateRequest(
    DateTimeOffset? ExpiresAtUtc = null,
    long? ExpectedVersion = null);

public sealed record BulkPasskeyRevokeItem(
    string Passkey,
    long? ExpectedVersion = null);

public sealed record BulkPasskeyRotateItem(
    string Passkey,
    DateTimeOffset? ExpiresAtUtc = null,
    long? ExpectedVersion = null);

[Obsolete("Use TrackerAccessRightsUpsertRequest. UserPermissionUpsertRequest is a compatibility alias and will be removed in a future release.")]
public sealed record UserPermissionUpsertRequest(
    bool CanLeech,
    bool CanSeed,
    bool CanScrape,
    bool CanUsePrivateTracker,
    long? ExpectedVersion = null);

[Obsolete("Use BulkTrackerAccessRightsUpsertItem. BulkUserPermissionUpsertItem is a compatibility alias and will be removed in a future release.")]
public sealed record BulkUserPermissionUpsertItem(
    Guid UserId,
    bool CanLeech,
    bool CanSeed,
    bool CanScrape,
    bool CanUsePrivateTracker,
    long? ExpectedVersion = null);

public sealed record BanRuleUpsertRequest(
    string Reason,
    DateTimeOffset? ExpiresAtUtc,
    long? ExpectedVersion = null);

public sealed record BulkBanRuleUpsertItem(
    string Scope,
    string Subject,
    string Reason,
    DateTimeOffset? ExpiresAtUtc,
    long? ExpectedVersion = null);

public sealed record BanRuleExpireRequest(
    DateTimeOffset ExpiresAtUtc,
    long? ExpectedVersion = null);

public sealed record BulkBanRuleExpireItem(
    string Scope,
    string Subject,
    DateTimeOffset ExpiresAtUtc,
    long? ExpectedVersion = null);

public sealed record BulkBanRuleDeleteItem(
    string Scope,
    string Subject,
    long? ExpectedVersion = null);

public sealed record AdminMutationContext(
    string ActorId,
    string ActorRole,
    string CorrelationId,
    string? RequestId,
    string? IpAddress,
    string? UserAgent);

public enum TrackerNodeConfigurationApplyMode
{
    Dynamic,
    RestartRecommended,
    StartupOnly
}

public enum TrackerConfigValidationSeverity
{
    Warning,
    Error
}

public sealed record TrackerConfigValidationIssueDto(
    string Code,
    string Path,
    TrackerConfigValidationSeverity Severity,
    string Message);

public sealed record TrackerNodeIdentityConfig(
    string NodeId,
    string NodeName,
    string Environment,
    string Region,
    string PublicBaseUrl,
    string InternalBaseUrl,
    bool SupportsHttp,
    bool SupportsUdp,
    bool SupportsPrivateTracker,
    bool SupportsPublicTracker);

public sealed record HttpTrackerConfig(
    bool EnableAnnounce,
    bool EnableScrape,
    string AnnounceRoute,
    string PrivateAnnounceRoute,
    string ScrapeRoute,
    int DefaultAnnounceIntervalSeconds,
    int MinAnnounceIntervalSeconds,
    int DefaultNumWant,
    int MaxNumWant,
    bool CompactResponsesByDefault,
    bool AllowNonCompactResponses,
    bool AllowPasskeyInPath,
    bool AllowPasskeyInQuery,
    bool AllowClientIpOverride,
    bool EmitWarningMessages);

public sealed record UdpTrackerConfig(
    bool Enabled,
    string BindAddress,
    int Port,
    int ConnectionTimeoutSeconds,
    int ReceiveBufferSize,
    int MaxDatagramSize,
    bool EnableScrape,
    int MaxScrapeInfoHashes);

public sealed record RuntimeStoreConfig(
    int ShardCount,
    int PeerTtlSeconds,
    int CleanupIntervalSeconds,
    int MaxPeersPerResponse,
    int? MaxPeersPerSwarm,
    bool PreferLocalShardPeers,
    bool EnableCompletedAccounting,
    bool EnableIPv6Peers);

public sealed record TrackerPolicyConfig(
    bool EnablePublicTracker,
    bool EnablePrivateTracker,
    bool RequirePasskeyForPrivateTracker,
    bool AllowPublicScrape,
    bool AllowPrivateScrape,
    string DefaultTorrentVisibility,
    string StrictnessProfile,
    string CompatibilityMode);

public sealed record RedisCoordinationConfig(
    bool Enabled,
    string Configuration,
    string KeyPrefix,
    int PolicyCacheTtlSeconds,
    int SnapshotCacheTtlSeconds,
    string InvalidationChannel,
    int HeartbeatTtlSeconds,
    int OwnershipLeaseDurationSeconds,
    int OwnershipRefreshIntervalSeconds,
    int SwarmSummaryPublishIntervalSeconds,
    int SwarmSummaryTtlSeconds);

public sealed record PostgresPersistenceConfig(
    bool Enabled,
    string ConnectionString,
    bool MigrateOnStart,
    bool PersistTelemetry,
    bool PersistAudit,
    int TelemetryBatchSize,
    int TelemetryFlushIntervalMilliseconds);

public sealed record AbuseProtectionConfig(
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

public sealed record ObservabilityConfig(
    bool EnableHealthEndpoints,
    bool EnableMetrics,
    bool EnableTracing,
    bool EnableDiagnosticsEndpoints,
    string LiveRoute,
    string ReadyRoute,
    string StartupRoute);

public sealed record TrackerNodeConfigurationDocument(
    TrackerNodeIdentityConfig Identity,
    HttpTrackerConfig Http,
    UdpTrackerConfig Udp,
    RuntimeStoreConfig Runtime,
    TrackerPolicyConfig Policy,
    RedisCoordinationConfig Redis,
    PostgresPersistenceConfig Postgres,
    AbuseProtectionConfig AbuseProtection,
    ObservabilityConfig Observability);

public sealed record TrackerNodeConfigurationDto(
    string NodeKey,
    TrackerNodeConfigurationDocument Configuration,
    long Version,
    DateTimeOffset UpdatedAtUtc,
    string UpdatedBy,
    TrackerNodeConfigurationApplyMode ApplyMode,
    bool RequiresRestart);

public sealed record TrackerNodeConfigurationUpsertRequest(
    TrackerNodeConfigurationDocument Configuration,
    TrackerNodeConfigurationApplyMode ApplyMode = TrackerNodeConfigurationApplyMode.RestartRecommended,
    long? ExpectedVersion = null);

public sealed record TrackerNodeConfigurationValidationResultDto(
    bool IsValid,
    IReadOnlyCollection<TrackerConfigValidationIssueDto> Issues);
