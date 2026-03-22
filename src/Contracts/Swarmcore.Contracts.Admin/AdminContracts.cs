namespace Swarmcore.Contracts.Admin;

public sealed record NodeHealthDto(string NodeId, string Region, bool Ready, DateTimeOffset ObservedAtUtc);

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
    long Version);

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
