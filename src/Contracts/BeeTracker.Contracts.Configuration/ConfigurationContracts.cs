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

public sealed record UserPermissionUpsertRequest(
    bool CanLeech,
    bool CanSeed,
    bool CanScrape,
    bool CanUsePrivateTracker,
    long? ExpectedVersion = null);

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
