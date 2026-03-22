namespace Tracker.ConfigurationService.Infrastructure;

internal sealed class TorrentConfigurationEntity
{
    public Guid Id { get; set; }
    public string InfoHash { get; set; } = string.Empty;
    public bool IsPrivate { get; set; }
    public bool IsEnabled { get; set; }
    public TorrentPolicyEntity? Policy { get; set; }
}

internal sealed class TorrentPolicyEntity
{
    public Guid TorrentId { get; set; }
    public int AnnounceIntervalSeconds { get; set; }
    public int MinAnnounceIntervalSeconds { get; set; }
    public int DefaultNumWant { get; set; }
    public int MaxNumWant { get; set; }
    public bool AllowScrape { get; set; }
    public string? WarningMessage { get; set; }
    public long RowVersion { get; set; }
    public TorrentConfigurationEntity Torrent { get; set; } = null!;
}

internal sealed class PasskeyCredentialEntity
{
    public string Passkey { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public long RowVersion { get; set; }
}

internal sealed class UserPermissionEntity
{
    public Guid UserId { get; set; }
    public bool CanLeech { get; set; }
    public bool CanSeed { get; set; }
    public bool CanScrape { get; set; }
    public bool CanUsePrivateTracker { get; set; }
    public long RowVersion { get; set; }
}

internal sealed class BanRuleEntity
{
    public string Scope { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime? ExpiresAtUtc { get; set; }
    public long RowVersion { get; set; }
}

internal sealed class AuditRecordEntity
{
    public Guid Id { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string ActorRole { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string? RequestId { get; set; }
    public string Result { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
}

internal sealed class MaintenanceRunEntity
{
    public Guid Id { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public DateTime RequestedAtUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}
