namespace BeeTracker.Contracts.Identity;

// ─── Account State ──────────────────────────────────────────────────────────

public enum AdminAccountState
{
    PendingActivation = 0,
    Active = 1,
    Suspended = 2,
    Deactivated = 3,
    ReactivationPending = 4,
    PasswordResetPending = 5,
    Locked = 6,
}

// ─── Registration ───────────────────────────────────────────────────────────

public sealed record RegisterAdminRequest(
    string UserName,
    string Email,
    string Password,
    string ConfirmPassword);

public sealed record RegisterAdminResponse(
    string UserId,
    bool RequiresActivation);

// ─── Activation ─────────────────────────────────────────────────────────────

public sealed record ActivateAdminRequest(string Token);

public sealed record ActivateAdminResponse(bool Success, string Message);

// ─── Reactivation ───────────────────────────────────────────────────────────

public sealed record ReactivateAdminRequestDto(string Email);

public sealed record ReactivateAdminConfirmRequest(string Token);

// ─── Password Reset ─────────────────────────────────────────────────────────

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(
    string Token,
    string NewPassword,
    string ConfirmNewPassword);

// ─── Password Change ────────────────────────────────────────────────────────

public sealed record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword,
    string ConfirmNewPassword);

// ─── Profile ────────────────────────────────────────────────────────────────

public sealed record AdminProfileResponse(
    string UserId,
    string UserName,
    string Email,
    AdminAccountState AccountState,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastLoginAtUtc);

// ─── State Transition ───────────────────────────────────────────────────────

public sealed record AccountStateTransitionResponse(
    bool Success,
    AdminAccountState NewState,
    string Message);

// ─── Error ──────────────────────────────────────────────────────────────────

public sealed record SelfServiceErrorResponse(
    string Code,
    string Message,
    IReadOnlyList<string>? Details);
