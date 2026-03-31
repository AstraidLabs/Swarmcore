using BeeTracker.BuildingBlocks.Domain.Primitives;

namespace Audit.Domain;

public enum AuditOutcome
{
    Success = 0,
    Failure = 1,
    Denied = 2,
    Error = 3,
}

public static class AuditAction
{
    public const string AdminRegistrationRequested = "admin.registration.requested";
    public const string AdminRegistrationCompleted = "admin.registration.completed";
    public const string AdminActivationRequested = "admin.activation.requested";
    public const string AdminActivationSucceeded = "admin.activation.succeeded";
    public const string AdminActivationFailed = "admin.activation.failed";
    public const string AdminReactivationRequested = "admin.reactivation.requested";
    public const string AdminReactivationSucceeded = "admin.reactivation.succeeded";
    public const string AdminPasswordResetRequested = "admin.password_reset.requested";
    public const string AdminPasswordResetCompleted = "admin.password_reset.completed";
    public const string AdminPasswordChanged = "admin.password.changed";
    public const string AdminLoginSucceeded = "admin.login.succeeded";
    public const string AdminLoginFailed = "admin.login.failed";
    public const string AdminAccountLocked = "admin.account.locked";
    public const string EmailSendRequested = "email.send.requested";
    public const string EmailSendSucceeded = "email.send.succeeded";
    public const string EmailSendFailed = "email.send.failed";
    public const string AdminAccountSuspended = "admin.account.suspended";
    public const string AdminAccountDeactivated = "admin.account.deactivated";
    public const string AdminAccountReactivationConfirmed = "admin.account.reactivation_confirmed";
    public const string AdminAccountUnlocked = "admin.account.unlocked";
    public const string AdminAccountActivatedByAdmin = "admin.account.activated_by_admin";
    public const string AdminAccountDeactivatedByAdmin = "admin.account.deactivated_by_admin";
    public const string AdminEmailChanged = "admin.email.changed";
    public const string AdminPasswordResetByAdmin = "admin.password_reset.by_admin";
    public const string AdminUserCreated = "admin.user.created";
    public const string AdminUserUpdated = "admin.user.updated";
    public const string AdminRolesAssigned = "admin.roles.assigned";
    public const string AdminRoleCreated = "admin.role.created";
    public const string AdminRoleUpdated = "admin.role.updated";
    public const string AdminRoleDeleted = "admin.role.deleted";
    public const string AdminPermissionGroupCreated = "admin.permission_group.created";
    public const string AdminPermissionGroupUpdated = "admin.permission_group.updated";
    public const string AdminPermissionGroupDeleted = "admin.permission_group.deleted";
    public const string AdminProfileUpdated = "admin.profile.updated";

    // ─── Governance ─────────────────────────────────────────────────────
    public const string GovernanceUpdated = "governance.state.updated";
    public const string GovernanceRestored = "governance.state.restored";
    public const string GovernanceAnnounceDisabledChanged = "governance.announce_disabled.changed";
    public const string GovernanceScrapeDisabledChanged = "governance.scrape_disabled.changed";
    public const string GovernanceMaintenanceModeChanged = "governance.maintenance_mode.changed";
    public const string GovernanceReadOnlyModeChanged = "governance.read_only_mode.changed";
    public const string GovernanceEmergencyAbuseMitigationChanged = "governance.emergency_abuse_mitigation.changed";
    public const string GovernanceUdpDisabledChanged = "governance.udp_disabled.changed";
    public const string GovernanceIPv6FrozenChanged = "governance.ipv6_frozen.changed";
    public const string GovernancePolicyFreezeModeChanged = "governance.policy_freeze_mode.changed";
    public const string GovernanceCompatibilityModeChanged = "governance.compatibility_mode.changed";
    public const string GovernanceStrictnessProfileChanged = "governance.strictness_profile.changed";
}

public sealed class AuditRecord : Entity<Guid>
{
    private AuditRecord(
        Guid id,
        string action,
        string? actorId,
        string? targetUserId,
        string? correlationId,
        string? ipAddress,
        string? userAgent,
        AuditOutcome outcome,
        string? reasonCode,
        string? metadataJson,
        DateTimeOffset occurredAtUtc)
        : base(id)
    {
        Action = action;
        ActorId = actorId;
        TargetUserId = targetUserId;
        CorrelationId = correlationId;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        Outcome = outcome;
        ReasonCode = reasonCode;
        MetadataJson = metadataJson;
        OccurredAtUtc = occurredAtUtc;
    }

    public string Action { get; }
    public string? ActorId { get; }
    public string? TargetUserId { get; }
    public string? CorrelationId { get; }
    public string? IpAddress { get; }
    public string? UserAgent { get; }
    public AuditOutcome Outcome { get; }
    public string? ReasonCode { get; }
    public string? MetadataJson { get; }
    public DateTimeOffset OccurredAtUtc { get; }

    public static AuditRecord Create(
        string action,
        string? actorId,
        string? targetUserId,
        string? correlationId,
        string? ipAddress,
        string? userAgent,
        AuditOutcome outcome,
        string? reasonCode = null,
        string? metadataJson = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        return new AuditRecord(
            Guid.NewGuid(),
            action,
            actorId,
            targetUserId,
            correlationId,
            ipAddress,
            userAgent,
            outcome,
            reasonCode,
            metadataJson,
            DateTimeOffset.UtcNow);
    }
}
