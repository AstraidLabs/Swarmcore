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
