using Identity.SelfService.Domain;
using MediatR;

namespace Identity.SelfService.Application;

// ─── Commands ──────────────────────────────────────────────────────────────────

public sealed record RegisterAdminCommand(
    string UserName,
    string Email,
    string Password,
    string ConfirmPassword,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId) : IRequest<SelfServiceResult>;

public sealed record ActivateAdminCommand(
    string Token,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId) : IRequest<SelfServiceResult>;

public sealed record RequestReactivationCommand(
    string Email,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId) : IRequest<SelfServiceResult>;

public sealed record ConfirmReactivationCommand(
    string Token,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId) : IRequest<SelfServiceResult>;

public sealed record ForgotPasswordCommand(
    string Email,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId) : IRequest<SelfServiceResult>;

public sealed record ResetPasswordCommand(
    string Token,
    string NewPassword,
    string ConfirmNewPassword,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId) : IRequest<SelfServiceResult>;

public sealed record ChangePasswordCommand(
    string UserId,
    string CurrentPassword,
    string NewPassword,
    string ConfirmNewPassword,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId) : IRequest<SelfServiceResult>;

// ─── Queries ───────────────────────────────────────────────────────────────────

public sealed record GetAdminProfileQuery(string UserId) : IRequest<AdminProfileDto?>;

// ─── Result ────────────────────────────────────────────────────────────────────

public sealed record SelfServiceResult
{
    public bool Success { get; }
    public string? ErrorCode { get; }
    public IReadOnlyList<string> ErrorMessages { get; }
    public string? UserId { get; }

    private SelfServiceResult(bool success, string? errorCode, IReadOnlyList<string> errorMessages, string? userId)
    {
        Success = success;
        ErrorCode = errorCode;
        ErrorMessages = errorMessages;
        UserId = userId;
    }

    public static SelfServiceResult Ok(string? userId)
        => new(true, null, Array.Empty<string>(), userId);

    public static SelfServiceResult Fail(string code, IReadOnlyList<string> messages)
        => new(false, code, messages, null);

    public static SelfServiceResult Fail(string code, string message)
        => new(false, code, new[] { message }, null);
}

// ─── Repository Contracts ──────────────────────────────────────────────────────

public interface IAdminAccountRepository
{
    Task<AdminAccountState?> GetAccountStateAsync(string userId, CancellationToken ct);
    Task SetAccountStateAsync(string userId, AdminAccountState state, CancellationToken ct);
    Task<string?> GetUserIdByEmailAsync(string email, CancellationToken ct);
    Task<DateTimeOffset?> GetCreatedAtUtcAsync(string userId, CancellationToken ct);
    Task<DateTimeOffset?> GetLastLoginAtUtcAsync(string userId, CancellationToken ct);
    Task RecordLoginAsync(string userId, DateTimeOffset now, CancellationToken ct);
}

public interface IVerificationTokenRepository
{
    Task CreateAsync(VerificationToken token, CancellationToken ct);
    Task<VerificationToken?> FindValidByHashAsync(string tokenHash, VerificationTokenPurpose purpose, CancellationToken ct);
    Task RevokeAllForUserAndPurposeAsync(string userId, VerificationTokenPurpose purpose, DateTimeOffset now, CancellationToken ct);
    Task ConsumeAsync(Guid tokenId, DateTimeOffset now, CancellationToken ct);
}

// ─── Token Hasher Contract ─────────────────────────────────────────────────────

public interface ITokenHasher
{
    string Hash(string rawToken);
    string GenerateRawToken();
}
