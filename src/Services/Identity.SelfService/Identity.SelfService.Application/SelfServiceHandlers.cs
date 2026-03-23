using Audit.Application;
using Audit.Domain;
using Identity.SelfService.Domain;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Notification.Application;

namespace Identity.SelfService.Application;

public sealed class RegisterAdminHandler(
    UserManager<IdentityUser> userManager,
    IAdminAccountRepository accountRepo,
    IVerificationTokenRepository tokenRepo,
    ITokenHasher tokenHasher,
    IEmailDispatchService emailDispatch,
    IAuditChannelWriter auditWriter,
    ILogger<RegisterAdminHandler> logger) : IRequestHandler<RegisterAdminCommand, SelfServiceResult>
{
    public async Task<SelfServiceResult> Handle(RegisterAdminCommand request, CancellationToken ct)
    {
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");

        if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
            return SelfServiceResult.Fail("PASSWORD_MISMATCH", "Password and confirmation do not match.");

        var existingByName = await userManager.FindByNameAsync(request.UserName);
        if (existingByName is not null)
            return SelfServiceResult.Fail("USERNAME_TAKEN", "This username is not available.");

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var existingByEmail = await userManager.FindByEmailAsync(request.Email);
            if (existingByEmail is not null)
                return SelfServiceResult.Fail("EMAIL_TAKEN", "This email is already registered.");
        }

        auditWriter.TryWrite(AuditRecord.Create(
            AuditAction.AdminRegistrationRequested, null, null, correlationId,
            request.IpAddress, request.UserAgent, AuditOutcome.Success, null, null));

        var user = new IdentityUser
        {
            UserName = request.UserName,
            Email = request.Email,
            EmailConfirmed = false
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            var errors = createResult.Errors.Select(e => e.Description).ToList();
            auditWriter.TryWrite(AuditRecord.Create(
                AuditAction.AdminRegistrationRequested, null, user.Id, correlationId,
                request.IpAddress, request.UserAgent, AuditOutcome.Failure, "IDENTITY_CREATE_FAILED",
                System.Text.Json.JsonSerializer.Serialize(new { errors })));
            return SelfServiceResult.Fail("REGISTRATION_FAILED", errors);
        }

        await accountRepo.SetAccountStateAsync(user.Id, AdminAccountState.PendingActivation, ct);

        await tokenRepo.RevokeAllForUserAndPurposeAsync(user.Id, VerificationTokenPurpose.AccountActivation, DateTimeOffset.UtcNow, ct);

        var rawToken = tokenHasher.GenerateRawToken();
        var tokenHash = tokenHasher.Hash(rawToken);
        var verificationToken = VerificationToken.Create(
            user.Id, VerificationTokenPurpose.AccountActivation, tokenHash,
            DateTimeOffset.UtcNow.AddHours(24));
        await tokenRepo.CreateAsync(verificationToken, ct);

        await emailDispatch.EnqueueAsync(new EmailEnvelope(
            request.Email,
            EmailTemplateName.AdminRegistration,
            new Dictionary<string, object>
            {
                ["UserName"] = request.UserName,
                ["ActivationToken"] = rawToken,
                ["ExpiresInHours"] = "24"
            },
            correlationId), ct);

        auditWriter.TryWrite(AuditRecord.Create(
            AuditAction.AdminRegistrationCompleted, user.Id, user.Id, correlationId,
            request.IpAddress, request.UserAgent, AuditOutcome.Success, null, null));

        logger.LogInformation("Admin registration completed for user {UserName}, activation required.", request.UserName);

        return SelfServiceResult.Ok(user.Id);
    }
}

public sealed class ActivateAdminHandler(
    IVerificationTokenRepository tokenRepo,
    IAdminAccountRepository accountRepo,
    ITokenHasher tokenHasher,
    UserManager<IdentityUser> userManager,
    IEmailDispatchService emailDispatch,
    IAuditChannelWriter auditWriter,
    ILogger<ActivateAdminHandler> logger) : IRequestHandler<ActivateAdminCommand, SelfServiceResult>
{
    public async Task<SelfServiceResult> Handle(ActivateAdminCommand request, CancellationToken ct)
    {
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");

        auditWriter.TryWrite(AuditRecord.Create(
            AuditAction.AdminActivationRequested, null, null, correlationId,
            request.IpAddress, request.UserAgent, AuditOutcome.Success, null, null));

        var tokenHash = tokenHasher.Hash(request.Token);
        var token = await tokenRepo.FindValidByHashAsync(tokenHash, VerificationTokenPurpose.AccountActivation, ct);

        if (token is null || !token.IsValid(DateTimeOffset.UtcNow))
        {
            auditWriter.TryWrite(AuditRecord.Create(
                AuditAction.AdminActivationFailed, null, null, correlationId,
                request.IpAddress, request.UserAgent, AuditOutcome.Failure, "INVALID_TOKEN", null));
            return SelfServiceResult.Fail("INVALID_TOKEN", "Activation token is invalid or expired.");
        }

        var currentState = await accountRepo.GetAccountStateAsync(token.UserId, ct);
        if (currentState is not AdminAccountState.PendingActivation)
        {
            auditWriter.TryWrite(AuditRecord.Create(
                AuditAction.AdminActivationFailed, null, token.UserId, correlationId,
                request.IpAddress, request.UserAgent, AuditOutcome.Failure, "INVALID_STATE",
                $"Current state: {currentState}"));
            return SelfServiceResult.Fail("INVALID_STATE", "Account is not in a state that allows activation.");
        }

        token.Consume(DateTimeOffset.UtcNow);
        await tokenRepo.ConsumeAsync(token.Id, DateTimeOffset.UtcNow, ct);

        var user = await userManager.FindByIdAsync(token.UserId);
        if (user is not null)
        {
            user.EmailConfirmed = true;
            await userManager.UpdateAsync(user);
        }

        await accountRepo.SetAccountStateAsync(token.UserId, AdminAccountState.Active, ct);

        if (user?.Email is not null)
        {
            await emailDispatch.EnqueueAsync(new EmailEnvelope(
                user.Email,
                EmailTemplateName.AdminActivation,
                new Dictionary<string, object>
                {
                    ["UserName"] = user.UserName ?? "Admin"
                },
                correlationId), ct);
        }

        auditWriter.TryWrite(AuditRecord.Create(
            AuditAction.AdminActivationSucceeded, token.UserId, token.UserId, correlationId,
            request.IpAddress, request.UserAgent, AuditOutcome.Success, null, null));

        logger.LogInformation("Admin account {UserId} activated successfully.", token.UserId);

        return SelfServiceResult.Ok(token.UserId);
    }
}

public sealed class RequestReactivationHandler(
    IAdminAccountRepository accountRepo,
    IVerificationTokenRepository tokenRepo,
    ITokenHasher tokenHasher,
    UserManager<IdentityUser> userManager,
    IEmailDispatchService emailDispatch,
    IAuditChannelWriter auditWriter,
    ILogger<RequestReactivationHandler> logger) : IRequestHandler<RequestReactivationCommand, SelfServiceResult>
{
    public async Task<SelfServiceResult> Handle(RequestReactivationCommand request, CancellationToken ct)
    {
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");

        // Do not leak account existence - always return success-like response
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            logger.LogDebug("Reactivation requested for non-existent email.");
            return SelfServiceResult.Ok(null);
        }

        var currentState = await accountRepo.GetAccountStateAsync(user.Id, ct);
        if (currentState is not AdminAccountState.Deactivated)
        {
            logger.LogDebug("Reactivation requested for account in state {State}.", currentState);
            return SelfServiceResult.Ok(null);
        }

        await accountRepo.SetAccountStateAsync(user.Id, AdminAccountState.ReactivationPending, ct);

        auditWriter.TryWrite(AuditRecord.Create(
            AuditAction.AdminReactivationRequested, user.Id, user.Id, correlationId,
            request.IpAddress, request.UserAgent, AuditOutcome.Success, null, null));

        await tokenRepo.RevokeAllForUserAndPurposeAsync(user.Id, VerificationTokenPurpose.AccountReactivation, DateTimeOffset.UtcNow, ct);

        var rawToken = tokenHasher.GenerateRawToken();
        var tokenHash = tokenHasher.Hash(rawToken);
        var verificationToken = VerificationToken.Create(
            user.Id, VerificationTokenPurpose.AccountReactivation, tokenHash,
            DateTimeOffset.UtcNow.AddHours(48));
        await tokenRepo.CreateAsync(verificationToken, ct);

        await emailDispatch.EnqueueAsync(new EmailEnvelope(
            request.Email,
            EmailTemplateName.AdminReactivation,
            new Dictionary<string, object>
            {
                ["UserName"] = user.UserName ?? "Admin",
                ["ReactivationToken"] = rawToken,
                ["ExpiresInHours"] = "48"
            },
            correlationId), ct);

        logger.LogInformation("Reactivation email enqueued for user {UserId}.", user.Id);

        return SelfServiceResult.Ok(user.Id);
    }
}

public sealed class ConfirmReactivationHandler(
    IVerificationTokenRepository tokenRepo,
    IAdminAccountRepository accountRepo,
    ITokenHasher tokenHasher,
    UserManager<IdentityUser> userManager,
    IEmailDispatchService emailDispatch,
    IAuditChannelWriter auditWriter,
    ILogger<ConfirmReactivationHandler> logger) : IRequestHandler<ConfirmReactivationCommand, SelfServiceResult>
{
    public async Task<SelfServiceResult> Handle(ConfirmReactivationCommand request, CancellationToken ct)
    {
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");
        var tokenHash = tokenHasher.Hash(request.Token);
        var token = await tokenRepo.FindValidByHashAsync(tokenHash, VerificationTokenPurpose.AccountReactivation, ct);

        if (token is null || !token.IsValid(DateTimeOffset.UtcNow))
            return SelfServiceResult.Fail("INVALID_TOKEN", "Reactivation token is invalid or expired.");

        var currentState = await accountRepo.GetAccountStateAsync(token.UserId, ct);
        if (currentState is not AdminAccountState.ReactivationPending)
            return SelfServiceResult.Fail("INVALID_STATE", "Account is not pending reactivation.");

        token.Consume(DateTimeOffset.UtcNow);
        await tokenRepo.ConsumeAsync(token.Id, DateTimeOffset.UtcNow, ct);
        await accountRepo.SetAccountStateAsync(token.UserId, AdminAccountState.Active, ct);

        auditWriter.TryWrite(AuditRecord.Create(
            AuditAction.AdminReactivationSucceeded, token.UserId, token.UserId, correlationId,
            request.IpAddress, request.UserAgent, AuditOutcome.Success, null, null));

        var user = await userManager.FindByIdAsync(token.UserId);
        if (user?.Email is not null)
        {
            await emailDispatch.EnqueueAsync(new EmailEnvelope(
                user.Email,
                EmailTemplateName.AdminActivation,
                new Dictionary<string, object>
                {
                    ["UserName"] = user.UserName ?? "Admin",
                    ["Message"] = "Your account has been reactivated."
                },
                correlationId), ct);
        }

        logger.LogInformation("Admin account {UserId} reactivated successfully.", token.UserId);

        return SelfServiceResult.Ok(token.UserId);
    }
}

public sealed class ForgotPasswordHandler(
    IAdminAccountRepository accountRepo,
    IVerificationTokenRepository tokenRepo,
    ITokenHasher tokenHasher,
    UserManager<IdentityUser> userManager,
    IEmailDispatchService emailDispatch,
    IAuditChannelWriter auditWriter,
    ILogger<ForgotPasswordHandler> logger) : IRequestHandler<ForgotPasswordCommand, SelfServiceResult>
{
    public async Task<SelfServiceResult> Handle(ForgotPasswordCommand request, CancellationToken ct)
    {
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");

        // Always return success to avoid leaking account existence
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            logger.LogDebug("Password reset requested for non-existent email.");
            return SelfServiceResult.Ok(null);
        }

        var currentState = await accountRepo.GetAccountStateAsync(user.Id, ct);
        if (currentState is not (AdminAccountState.Active or AdminAccountState.PasswordResetPending))
        {
            logger.LogDebug("Password reset requested for account in state {State}.", currentState);
            return SelfServiceResult.Ok(null);
        }

        await accountRepo.SetAccountStateAsync(user.Id, AdminAccountState.PasswordResetPending, ct);

        auditWriter.TryWrite(AuditRecord.Create(
            AuditAction.AdminPasswordResetRequested, user.Id, user.Id, correlationId,
            request.IpAddress, request.UserAgent, AuditOutcome.Success, null, null));

        await tokenRepo.RevokeAllForUserAndPurposeAsync(user.Id, VerificationTokenPurpose.PasswordReset, DateTimeOffset.UtcNow, ct);

        var rawToken = tokenHasher.GenerateRawToken();
        var tokenHash = tokenHasher.Hash(rawToken);
        var verificationToken = VerificationToken.Create(
            user.Id, VerificationTokenPurpose.PasswordReset, tokenHash,
            DateTimeOffset.UtcNow.AddHours(2));
        await tokenRepo.CreateAsync(verificationToken, ct);

        await emailDispatch.EnqueueAsync(new EmailEnvelope(
            request.Email,
            EmailTemplateName.AdminPasswordReset,
            new Dictionary<string, object>
            {
                ["UserName"] = user.UserName ?? "Admin",
                ["ResetToken"] = rawToken,
                ["ExpiresInHours"] = "2"
            },
            correlationId), ct);

        logger.LogInformation("Password reset email enqueued for user {UserId}.", user.Id);

        return SelfServiceResult.Ok(user.Id);
    }
}

public sealed class ResetPasswordHandler(
    IVerificationTokenRepository tokenRepo,
    IAdminAccountRepository accountRepo,
    ITokenHasher tokenHasher,
    UserManager<IdentityUser> userManager,
    IEmailDispatchService emailDispatch,
    IAuditChannelWriter auditWriter,
    ILogger<ResetPasswordHandler> logger) : IRequestHandler<ResetPasswordCommand, SelfServiceResult>
{
    public async Task<SelfServiceResult> Handle(ResetPasswordCommand request, CancellationToken ct)
    {
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");

        if (!string.Equals(request.NewPassword, request.ConfirmNewPassword, StringComparison.Ordinal))
            return SelfServiceResult.Fail("PASSWORD_MISMATCH", "Password and confirmation do not match.");

        var tokenHash = tokenHasher.Hash(request.Token);
        var token = await tokenRepo.FindValidByHashAsync(tokenHash, VerificationTokenPurpose.PasswordReset, ct);

        if (token is null || !token.IsValid(DateTimeOffset.UtcNow))
            return SelfServiceResult.Fail("INVALID_TOKEN", "Password reset token is invalid or expired.");

        var user = await userManager.FindByIdAsync(token.UserId);
        if (user is null)
            return SelfServiceResult.Fail("USER_NOT_FOUND", "User account not found.");

        // Use ASP.NET Identity's built-in reset token for the actual password change
        var identityResetToken = await userManager.GeneratePasswordResetTokenAsync(user);
        var resetResult = await userManager.ResetPasswordAsync(user, identityResetToken, request.NewPassword);

        if (!resetResult.Succeeded)
        {
            var errors = resetResult.Errors.Select(e => e.Description).ToList();
            return SelfServiceResult.Fail("RESET_FAILED", errors);
        }

        token.Consume(DateTimeOffset.UtcNow);
        await tokenRepo.ConsumeAsync(token.Id, DateTimeOffset.UtcNow, ct);
        await accountRepo.SetAccountStateAsync(token.UserId, AdminAccountState.Active, ct);

        auditWriter.TryWrite(AuditRecord.Create(
            AuditAction.AdminPasswordResetCompleted, token.UserId, token.UserId, correlationId,
            request.IpAddress, request.UserAgent, AuditOutcome.Success, null, null));

        if (user.Email is not null)
        {
            await emailDispatch.EnqueueAsync(new EmailEnvelope(
                user.Email,
                EmailTemplateName.AdminPasswordChanged,
                new Dictionary<string, object>
                {
                    ["UserName"] = user.UserName ?? "Admin",
                    ["Message"] = "Your password has been reset successfully."
                },
                correlationId), ct);
        }

        logger.LogInformation("Password reset completed for user {UserId}.", token.UserId);

        return SelfServiceResult.Ok(token.UserId);
    }
}

public sealed class ChangePasswordHandler(
    UserManager<IdentityUser> userManager,
    IAdminAccountRepository accountRepo,
    IEmailDispatchService emailDispatch,
    IAuditChannelWriter auditWriter,
    ILogger<ChangePasswordHandler> logger) : IRequestHandler<ChangePasswordCommand, SelfServiceResult>
{
    public async Task<SelfServiceResult> Handle(ChangePasswordCommand request, CancellationToken ct)
    {
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");

        if (!string.Equals(request.NewPassword, request.ConfirmNewPassword, StringComparison.Ordinal))
            return SelfServiceResult.Fail("PASSWORD_MISMATCH", "Password and confirmation do not match.");

        var user = await userManager.FindByIdAsync(request.UserId);
        if (user is null)
            return SelfServiceResult.Fail("USER_NOT_FOUND", "User account not found.");

        var currentState = await accountRepo.GetAccountStateAsync(request.UserId, ct);
        if (currentState is not AdminAccountState.Active)
            return SelfServiceResult.Fail("INVALID_STATE", "Account must be active to change password.");

        var changeResult = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!changeResult.Succeeded)
        {
            var errors = changeResult.Errors.Select(e => e.Description).ToList();

            auditWriter.TryWrite(AuditRecord.Create(
                AuditAction.AdminPasswordChanged, request.UserId, request.UserId, correlationId,
                request.IpAddress, request.UserAgent, AuditOutcome.Failure, "CHANGE_FAILED",
                System.Text.Json.JsonSerializer.Serialize(new { errors })));

            return SelfServiceResult.Fail("CHANGE_FAILED", errors);
        }

        await userManager.UpdateSecurityStampAsync(user);

        auditWriter.TryWrite(AuditRecord.Create(
            AuditAction.AdminPasswordChanged, request.UserId, request.UserId, correlationId,
            request.IpAddress, request.UserAgent, AuditOutcome.Success, null, null));

        if (user.Email is not null)
        {
            await emailDispatch.EnqueueAsync(new EmailEnvelope(
                user.Email,
                EmailTemplateName.AdminPasswordChanged,
                new Dictionary<string, object>
                {
                    ["UserName"] = user.UserName ?? "Admin",
                    ["Message"] = "Your password was changed. If you did not make this change, contact support immediately."
                },
                correlationId), ct);
        }

        logger.LogInformation("Password changed for user {UserId}.", request.UserId);

        return SelfServiceResult.Ok(request.UserId);
    }
}

public sealed class GetAdminProfileHandler(
    UserManager<IdentityUser> userManager,
    IAdminAccountRepository accountRepo) : IRequestHandler<GetAdminProfileQuery, AdminProfileDto?>
{
    public async Task<AdminProfileDto?> Handle(GetAdminProfileQuery request, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(request.UserId);
        if (user is null)
            return null;

        var state = await accountRepo.GetAccountStateAsync(request.UserId, ct);
        var roles = await userManager.GetRolesAsync(user);
        var createdAt = await accountRepo.GetCreatedAtUtcAsync(request.UserId, ct);
        var lastLogin = await accountRepo.GetLastLoginAtUtcAsync(request.UserId, ct);

        return new AdminProfileDto(
            user.Id,
            user.UserName ?? string.Empty,
            user.Email ?? string.Empty,
            state?.ToString() ?? "Unknown",
            roles.ToList().AsReadOnly(),
            createdAt ?? DateTimeOffset.MinValue,
            lastLogin);
    }
}

public sealed record AdminProfileDto(
    string UserId,
    string UserName,
    string Email,
    string AccountState,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastLoginAtUtc);
