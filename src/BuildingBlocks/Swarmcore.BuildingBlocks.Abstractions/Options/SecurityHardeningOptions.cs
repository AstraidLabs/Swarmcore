namespace Swarmcore.BuildingBlocks.Abstractions.Options;

/// <summary>
/// Security hardening options for passkey lifecycle management and credential safety.
/// </summary>
public sealed class SecurityHardeningOptions
{
    public const string SectionName = "Swarmcore:SecurityHardening";

    /// <summary>Whether to reject announces with expired passkeys (fail-closed). Default true.</summary>
    public bool RejectExpiredPasskeys { get; init; } = true;

    /// <summary>Whether to reject announces with revoked passkeys (fail-closed). Default true.</summary>
    public bool RejectRevokedPasskeys { get; init; } = true;

    /// <summary>Maximum number of active (non-revoked) passkeys per user. 0 = unlimited.</summary>
    public int MaxActivePasskeysPerUser { get; init; } = 5;

    /// <summary>Default passkey expiration in days. 0 = no default expiration.</summary>
    public int DefaultPasskeyExpirationDays { get; init; } = 0;

    /// <summary>Whether to require connection strings to be non-empty on startup.</summary>
    public bool RequireExplicitConnectionStrings { get; init; } = true;

    /// <summary>Whether to require the tracker to operate in private mode when passkey enforcement is active.</summary>
    public bool EnforcePrivateTrackerPasskeyConsistency { get; init; } = true;

    /// <summary>
    /// Grace period in seconds after passkey revocation during which cached entries may still resolve.
    /// After this period, the passkey must be fully purged from all cache layers.
    /// </summary>
    public int PasskeyRevocationGracePeriodSeconds { get; init; } = 30;

    /// <summary>Whether admin diagnostic endpoints require authentication context. Default true.</summary>
    public bool RequireAdminAuthForDiagnostics { get; init; } = true;

    /// <summary>Maximum allowed maintenance mode duration in minutes before automatic reactivation. 0 = no limit.</summary>
    public int MaxMaintenanceModeDurationMinutes { get; init; } = 0;

    /// <summary>Whether to log all passkey state transitions (create/revoke/rotate/expire) for audit.</summary>
    public bool AuditPasskeyStateTransitions { get; init; } = true;

    /// <summary>Whether to log all admin node state transitions for audit.</summary>
    public bool AuditNodeStateTransitions { get; init; } = true;
}
