using Audit.Application;
using Audit.Domain;
using BeeTracker.Contracts.Identity;
using Identity.SelfService.Domain;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Notification.Application;
using System.Text.Json;

using AdminAccountState = Identity.SelfService.Domain.AdminAccountState;
using SystemRoleNames = Identity.SelfService.Domain.SystemRoleNames;

namespace Identity.SelfService.Application;

// ─── Update Profile ─────────────────────────────────────────────────────────

public sealed class UpdateProfileHandler(
    IRbacService rbacService,
    IAuditChannelWriter auditWriter,
    ILogger<UpdateProfileHandler> logger) : IRequestHandler<UpdateProfileCommand, SelfServiceResult>
{
    public async Task<SelfServiceResult> Handle(UpdateProfileCommand request, CancellationToken ct)
    {
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");

        var profile = await rbacService.GetProfileDetailAsync(request.UserId, ct);
        if (profile is null)
            return SelfServiceResult.Fail("USER_NOT_FOUND", "User not found.");

        await rbacService.UpdateProfileAsync(request.UserId, request.DisplayName, request.TimeZone, ct);

        auditWriter.TryWrite(AuditRecord.Create(
            AuditAction.AdminProfileUpdated, request.UserId, request.UserId, correlationId,
            request.IpAddress, request.UserAgent, AuditOutcome.Success));

        logger.LogInformation("Profile updated for user {UserId}.", request.UserId);
        return SelfServiceResult.Ok(request.UserId);
    }
}

// ─── Change Email ───────────────────────────────────────────────────────────

public sealed class ChangeEmailHandler(
    UserManager<IdentityUser> userManager,
    IAdminAccountRepository accountRepo,
    IEmailDispatchService emailDispatch,
    IAuditChannelWriter auditWriter,
    ILogger<ChangeEmailHandler> logger) : IRequestHandler<ChangeEmailCommand, SelfServiceResult>
{
    public async Task<SelfServiceResult> Handle(ChangeEmailCommand request, CancellationToken ct)
    {
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");

        var user = await userManager.FindByIdAsync(request.UserId);
        if (user is null)
            return SelfServiceResult.Fail("USER_NOT_FOUND", "User not found.");

        var currentState = await accountRepo.GetAccountStateAsync(request.UserId, ct);
        if (currentState is not AdminAccountState.Active)
            return SelfServiceResult.Fail("INVALID_STATE", "Account must be active to change email.");

        if (!await userManager.CheckPasswordAsync(user, request.CurrentPassword))
            return SelfServiceResult.Fail("INVALID_PASSWORD", "Current password is incorrect.");

        var existingByEmail = await userManager.FindByEmailAsync(request.NewEmail);
        if (existingByEmail is not null && existingByEmail.Id != user.Id)
            return SelfServiceResult.Fail("EMAIL_TAKEN", "This email is already in use.");

        var oldEmail = user.Email;
        user.Email = request.NewEmail;
        user.EmailConfirmed = true;
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            var errors = updateResult.Errors.Select(e => e.Description).ToList();
            return SelfServiceResult.Fail("UPDATE_FAILED", errors);
        }

        auditWriter.TryWrite(AuditRecord.Create(
            AuditAction.AdminEmailChanged, request.UserId, request.UserId, correlationId,
            request.IpAddress, request.UserAgent, AuditOutcome.Success, null,
            System.Text.Json.JsonSerializer.Serialize(new { oldEmail, newEmail = request.NewEmail })));

        if (!string.IsNullOrWhiteSpace(oldEmail))
        {
            await emailDispatch.EnqueueAsync(new EmailEnvelope(
                oldEmail,
                EmailTemplateName.AdminSecurityAlert,
                new Dictionary<string, object>
                {
                    ["UserName"] = user.UserName ?? "Admin",
                    ["Message"] = $"Your email address has been changed to {request.NewEmail}. If you did not make this change, contact support immediately."
                },
                correlationId), ct);
        }

        await emailDispatch.EnqueueAsync(new EmailEnvelope(
            request.NewEmail,
            EmailTemplateName.AdminEmailChanged,
            new Dictionary<string, object>
            {
                ["UserName"] = user.UserName ?? "Admin",
                ["Message"] = "Your email address has been updated successfully."
            },
            correlationId), ct);

        logger.LogInformation("Email changed for user {UserId}.", request.UserId);
        return SelfServiceResult.Ok(request.UserId);
    }
}

// ─── Create Admin User ──────────────────────────────────────────────────────

public sealed class CreateAdminUserHandler(
    UserManager<IdentityUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IAdminAccountRepository accountRepo,
    IRbacService rbacService,
    IEmailDispatchService emailDispatch,
    IAuditChannelWriter auditWriter,
    ILogger<CreateAdminUserHandler> logger) : IRequestHandler<CreateAdminUserCommand, SelfServiceResult>
{
    public async Task<SelfServiceResult> Handle(CreateAdminUserCommand request, CancellationToken ct)
    {
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");
        var authorizationFailure = await RbacCommandAuthorization.EnsureAsync(rbacService, request.ActorId, PermissionCatalog.UsersCreate, ct);
        if (authorizationFailure is not null)
            return authorizationFailure;

        var existingByName = await userManager.FindByNameAsync(request.UserName);
        if (existingByName is not null)
            return SelfServiceResult.Fail("USERNAME_TAKEN", "This username is not available.");

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var existingByEmail = await userManager.FindByEmailAsync(request.Email);
            if (existingByEmail is not null)
                return SelfServiceResult.Fail("EMAIL_TAKEN", "This email is already registered.");
        }

        // Validate roles exist
        foreach (var roleName in request.Roles)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
                return SelfServiceResult.Fail("ROLE_NOT_FOUND", $"Role '{roleName}' does not exist.");
        }

        // Cannot assign SuperAdmin unless actor is SuperAdmin
        if (request.Roles.Contains(SystemRoleNames.SuperAdmin, StringComparer.OrdinalIgnoreCase))
        {
            if (!await rbacService.IsSuperAdminAsync(request.ActorId, ct))
                return SelfServiceResult.Fail("INSUFFICIENT_PERMISSIONS", "Only SuperAdmin can assign the SuperAdmin role.");
        }

        var user = new IdentityUser
        {
            UserName = request.UserName,
            Email = request.Email,
            EmailConfirmed = !string.IsNullOrWhiteSpace(request.Email)
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            var errors = createResult.Errors.Select(e => e.Description).ToList();
            return SelfServiceResult.Fail("CREATE_FAILED", errors);
        }

        await accountRepo.SetAccountStateAsync(user.Id, AdminAccountState.Active, ct);
        await rbacService.UpdateProfileAsync(
            user.Id,
            string.IsNullOrWhiteSpace(request.DisplayName) ? request.UserName : request.DisplayName,
            "UTC",
            ct);

        foreach (var roleName in request.Roles)
        {
            await userManager.AddToRoleAsync(user, roleName);
        }

        await rbacService.InvalidatePermissionSnapshotAsync(ct);

        auditWriter.TryWrite(AuditRecord.Create(
            AuditAction.AdminUserCreated, request.ActorId, user.Id, correlationId,
            request.IpAddress, request.UserAgent, AuditOutcome.Success, null,
            System.Text.Json.JsonSerializer.Serialize(new { request.UserName, request.Roles })));

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            await emailDispatch.EnqueueAsync(new EmailEnvelope(
                request.Email,
                EmailTemplateName.AdminUserCreatedNotification,
                new Dictionary<string, object>
                {
                    ["UserName"] = request.UserName,
                    ["Message"] = "An administrator account has been created for you."
                },
                correlationId), ct);
        }

        logger.LogInformation("Admin user {UserName} created by {ActorId}.", request.UserName, request.ActorId);
        return SelfServiceResult.Ok(user.Id);
    }
}

// ─── Update Admin User ──────────────────────────────────────────────────────

public sealed class UpdateAdminUserHandler(
    UserManager<IdentityUser> userManager,
    IRbacService rbacService,
    IAuditChannelWriter auditWriter,
    ILogger<UpdateAdminUserHandler> logger) : IRequestHandler<UpdateAdminUserCommand, SelfServiceResult>
{
    public async Task<SelfServiceResult> Handle(UpdateAdminUserCommand request, CancellationToken ct)
    {
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");
        var authorizationFailure = await RbacCommandAuthorization.EnsureAsync(rbacService, request.ActorId, PermissionCatalog.UsersEdit, ct);
        if (authorizationFailure is not null)
            return authorizationFailure;

        var user = await userManager.FindByIdAsync(request.TargetUserId);
        if (user is null)
            return SelfServiceResult.Fail("USER_NOT_FOUND", "User not found.");

        if (!string.IsNullOrWhiteSpace(request.Email) && !string.Equals(user.Email, request.Email, StringComparison.OrdinalIgnoreCase))
        {
            var existingByEmail = await userManager.FindByEmailAsync(request.Email);
            if (existingByEmail is not null && existingByEmail.Id != user.Id)
                return SelfServiceResult.Fail("EMAIL_TAKEN", "This email is already in use.");

            user.Email = request.Email;
            user.EmailConfirmed = true;
        }

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            var errors = updateResult.Errors.Select(e => e.Description).ToList();
            return SelfServiceResult.Fail("UPDATE_FAILED", errors);
        }

        await rbacService.UpdateProfileAsync(
            request.TargetUserId,
            string.IsNullOrWhiteSpace(request.DisplayName) ? user.UserName ?? request.TargetUserId : request.DisplayName,
            "UTC",
            ct);

        auditWriter.TryWrite(AuditRecord.Create(
            AuditAction.AdminUserUpdated, request.ActorId, request.TargetUserId, correlationId,
            request.IpAddress, request.UserAgent, AuditOutcome.Success));

        logger.LogInformation("Admin user {TargetUserId} updated by {ActorId}.", request.TargetUserId, request.ActorId);
        return SelfServiceResult.Ok(request.TargetUserId);
    }
}

// ─── Assign Roles ───────────────────────────────────────────────────────────

public sealed class AssignRolesHandler(
    UserManager<IdentityUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IRbacService rbacService,
    IAuditChannelWriter auditWriter,
    ILogger<AssignRolesHandler> logger) : IRequestHandler<AssignRolesCommand, SelfServiceResult>
{
    public async Task<SelfServiceResult> Handle(AssignRolesCommand request, CancellationToken ct)
    {
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");
        var authorizationFailure = await RbacCommandAuthorization.EnsureAsync(rbacService, request.ActorId, PermissionCatalog.UsersAssignRoles, ct);
        if (authorizationFailure is not null)
            return authorizationFailure;

        var user = await userManager.FindByIdAsync(request.TargetUserId);
        if (user is null)
            return SelfServiceResult.Fail("USER_NOT_FOUND", "User not found.");

        // Validate all roles exist
        foreach (var roleName in request.Roles)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
                return SelfServiceResult.Fail("ROLE_NOT_FOUND", $"Role '{roleName}' does not exist.");
        }

        // Cannot assign SuperAdmin unless actor is SuperAdmin
        if (request.Roles.Contains(SystemRoleNames.SuperAdmin, StringComparer.OrdinalIgnoreCase))
        {
            if (!await rbacService.IsSuperAdminAsync(request.ActorId, ct))
                return SelfServiceResult.Fail("INSUFFICIENT_PERMISSIONS", "Only SuperAdmin can assign the SuperAdmin role.");
        }

        // Protection: cannot remove SuperAdmin from last active SuperAdmin
        var currentRoles = await userManager.GetRolesAsync(user);
        if (currentRoles.Contains(SystemRoleNames.SuperAdmin, StringComparer.OrdinalIgnoreCase)
            && !request.Roles.Contains(SystemRoleNames.SuperAdmin, StringComparer.OrdinalIgnoreCase))
        {
            if (await rbacService.IsLastActiveSuperAdminAsync(request.TargetUserId, ct))
                return SelfServiceResult.Fail("LAST_SUPERADMIN", "Cannot remove SuperAdmin role from the last active SuperAdmin.");
        }

        // Remove old roles
        var rolesToRemove = currentRoles.Except(request.Roles, StringComparer.OrdinalIgnoreCase).ToList();
        if (rolesToRemove.Count > 0)
            await userManager.RemoveFromRolesAsync(user, rolesToRemove);

        // Add new roles
        var rolesToAdd = request.Roles.Except(currentRoles, StringComparer.OrdinalIgnoreCase).ToList();
        if (rolesToAdd.Count > 0)
            await userManager.AddToRolesAsync(user, rolesToAdd);

        await rbacService.InvalidatePermissionSnapshotAsync(ct);

        auditWriter.TryWrite(AuditRecord.Create(
            AuditAction.AdminRolesAssigned, request.ActorId, request.TargetUserId, correlationId,
            request.IpAddress, request.UserAgent, AuditOutcome.Success, null,
            System.Text.Json.JsonSerializer.Serialize(new { previousRoles = currentRoles, newRoles = request.Roles })));

        logger.LogInformation("Roles assigned to user {TargetUserId} by {ActorId}.", request.TargetUserId, request.ActorId);
        return SelfServiceResult.Ok(request.TargetUserId);
    }
}

// ─── Reset Password (Admin) ─────────────────────────────────────────────────

public sealed class ResetPasswordAdminHandler(
    UserManager<IdentityUser> userManager,
    IRbacService rbacService,
    IEmailDispatchService emailDispatch,
    IAuditChannelWriter auditWriter,
    ILogger<ResetPasswordAdminHandler> logger) : IRequestHandler<ResetPasswordAdminCommand, SelfServiceResult>
{
    public async Task<SelfServiceResult> Handle(ResetPasswordAdminCommand request, CancellationToken ct)
    {
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");
        var authorizationFailure = await RbacCommandAuthorization.EnsureAsync(rbacService, request.ActorId, PermissionCatalog.UsersResetPassword, ct);
        if (authorizationFailure is not null)
            return authorizationFailure;

        var user = await userManager.FindByIdAsync(request.TargetUserId);
        if (user is null)
            return SelfServiceResult.Fail("USER_NOT_FOUND", "User not found.");

        var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
        var resetResult = await userManager.ResetPasswordAsync(user, resetToken, request.NewPassword);
        if (!resetResult.Succeeded)
        {
            var errors = resetResult.Errors.Select(e => e.Description).ToList();
            return SelfServiceResult.Fail("RESET_FAILED", errors);
        }

        await userManager.UpdateSecurityStampAsync(user);
        await rbacService.InvalidatePermissionSnapshotAsync(ct);

        auditWriter.TryWrite(AuditRecord.Create(
            AuditAction.AdminPasswordResetByAdmin, request.ActorId, request.TargetUserId, correlationId,
            request.IpAddress, request.UserAgent, AuditOutcome.Success));

        if (user.Email is not null)
        {
            await emailDispatch.EnqueueAsync(new EmailEnvelope(
                user.Email,
                EmailTemplateName.AdminPasswordResetByAdmin,
                new Dictionary<string, object>
                {
                    ["UserName"] = user.UserName ?? "Admin",
                    ["Message"] = "Your password has been reset by an administrator. If you did not request this, contact support immediately."
                },
                correlationId), ct);
        }

        logger.LogInformation("Password reset for user {TargetUserId} by {ActorId}.", request.TargetUserId, request.ActorId);
        return SelfServiceResult.Ok(request.TargetUserId);
    }
}

// ─── Activate Account (Admin) ───────────────────────────────────────────────

public sealed class ActivateAccountAdminHandler(
    UserManager<IdentityUser> userManager,
    IAdminAccountRepository accountRepo,
    IRbacService rbacService,
    IEmailDispatchService emailDispatch,
    IAuditChannelWriter auditWriter,
    ILogger<ActivateAccountAdminHandler> logger) : IRequestHandler<ActivateAccountAdminCommand, SelfServiceResult>
{
    public async Task<SelfServiceResult> Handle(ActivateAccountAdminCommand request, CancellationToken ct)
    {
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");
        var authorizationFailure = await RbacCommandAuthorization.EnsureAsync(rbacService, request.ActorId, PermissionCatalog.UsersActivate, ct);
        if (authorizationFailure is not null)
            return authorizationFailure;

        var user = await userManager.FindByIdAsync(request.TargetUserId);
        if (user is null)
            return SelfServiceResult.Fail("USER_NOT_FOUND", "User not found.");

        var currentState = await accountRepo.GetAccountStateAsync(request.TargetUserId, ct);
        if (currentState is AdminAccountState.Active)
            return SelfServiceResult.Fail("ALREADY_ACTIVE", "Account is already active.");

        if (currentState is not (AdminAccountState.PendingActivation or AdminAccountState.Deactivated or AdminAccountState.Suspended or AdminAccountState.Locked))
            return SelfServiceResult.Fail("INVALID_STATE", $"Account cannot be activated from state '{currentState}'.");

        await accountRepo.SetAccountStateAsync(request.TargetUserId, AdminAccountState.Active, ct);
        await rbacService.InvalidatePermissionSnapshotAsync(ct);

        auditWriter.TryWrite(AuditRecord.Create(
            AuditAction.AdminAccountActivatedByAdmin, request.ActorId, request.TargetUserId, correlationId,
            request.IpAddress, request.UserAgent, AuditOutcome.Success));

        if (user.Email is not null)
        {
            await emailDispatch.EnqueueAsync(new EmailEnvelope(
                user.Email,
                EmailTemplateName.AdminAccountActivatedByAdmin,
                new Dictionary<string, object>
                {
                    ["UserName"] = user.UserName ?? "Admin",
                    ["Message"] = "Your account has been activated by an administrator."
                },
                correlationId), ct);
        }

        logger.LogInformation("Account {TargetUserId} activated by {ActorId}.", request.TargetUserId, request.ActorId);
        return SelfServiceResult.Ok(request.TargetUserId);
    }
}

// ─── Deactivate Account (Admin) ─────────────────────────────────────────────

public sealed class DeactivateAccountAdminHandler(
    UserManager<IdentityUser> userManager,
    IAdminAccountRepository accountRepo,
    IRbacService rbacService,
    IEmailDispatchService emailDispatch,
    IAuditChannelWriter auditWriter,
    ILogger<DeactivateAccountAdminHandler> logger) : IRequestHandler<DeactivateAccountAdminCommand, SelfServiceResult>
{
    public async Task<SelfServiceResult> Handle(DeactivateAccountAdminCommand request, CancellationToken ct)
    {
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");
        var authorizationFailure = await RbacCommandAuthorization.EnsureAsync(rbacService, request.ActorId, PermissionCatalog.UsersDeactivate, ct);
        if (authorizationFailure is not null)
            return authorizationFailure;

        var user = await userManager.FindByIdAsync(request.TargetUserId);
        if (user is null)
            return SelfServiceResult.Fail("USER_NOT_FOUND", "User not found.");

        // Protection: cannot deactivate last active SuperAdmin
        if (await rbacService.IsLastActiveSuperAdminAsync(request.TargetUserId, ct))
            return SelfServiceResult.Fail("LAST_SUPERADMIN", "Cannot deactivate the last active SuperAdmin.");

        var currentState = await accountRepo.GetAccountStateAsync(request.TargetUserId, ct);
        if (currentState is AdminAccountState.Deactivated)
            return SelfServiceResult.Fail("ALREADY_DEACTIVATED", "Account is already deactivated.");

        await accountRepo.SetAccountStateAsync(request.TargetUserId, AdminAccountState.Deactivated, ct);
        await rbacService.InvalidatePermissionSnapshotAsync(ct);

        auditWriter.TryWrite(AuditRecord.Create(
            AuditAction.AdminAccountDeactivatedByAdmin, request.ActorId, request.TargetUserId, correlationId,
            request.IpAddress, request.UserAgent, AuditOutcome.Success));

        if (user.Email is not null)
        {
            await emailDispatch.EnqueueAsync(new EmailEnvelope(
                user.Email,
                EmailTemplateName.AdminAccountDeactivatedByAdmin,
                new Dictionary<string, object>
                {
                    ["UserName"] = user.UserName ?? "Admin",
                    ["Message"] = "Your account has been deactivated by an administrator."
                },
                correlationId), ct);
        }

        logger.LogInformation("Account {TargetUserId} deactivated by {ActorId}.", request.TargetUserId, request.ActorId);
        return SelfServiceResult.Ok(request.TargetUserId);
    }
}

// ─── Lock Account ───────────────────────────────────────────────────────────

public sealed class LockAccountHandler(
    UserManager<IdentityUser> userManager,
    IAdminAccountRepository accountRepo,
    IRbacService rbacService,
    IEmailDispatchService emailDispatch,
    IAuditChannelWriter auditWriter,
    ILogger<LockAccountHandler> logger) : IRequestHandler<LockAccountCommand, SelfServiceResult>
{
    public async Task<SelfServiceResult> Handle(LockAccountCommand request, CancellationToken ct)
    {
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");
        var authorizationFailure = await RbacCommandAuthorization.EnsureAsync(rbacService, request.ActorId, PermissionCatalog.UsersDeactivate, ct);
        if (authorizationFailure is not null)
            return authorizationFailure;

        var user = await userManager.FindByIdAsync(request.TargetUserId);
        if (user is null)
            return SelfServiceResult.Fail("USER_NOT_FOUND", "User not found.");

        if (await rbacService.IsLastActiveSuperAdminAsync(request.TargetUserId, ct))
            return SelfServiceResult.Fail("LAST_SUPERADMIN", "Cannot lock the last active SuperAdmin.");

        var currentState = await accountRepo.GetAccountStateAsync(request.TargetUserId, ct);
        if (currentState is AdminAccountState.Locked)
            return SelfServiceResult.Fail("ALREADY_LOCKED", "Account is already locked.");

        await accountRepo.SetAccountStateAsync(request.TargetUserId, AdminAccountState.Locked, ct);
        await rbacService.InvalidatePermissionSnapshotAsync(ct);

        auditWriter.TryWrite(AuditRecord.Create(
            AuditAction.AdminAccountLocked, request.ActorId, request.TargetUserId, correlationId,
            request.IpAddress, request.UserAgent, AuditOutcome.Success));

        if (user.Email is not null)
        {
            await emailDispatch.EnqueueAsync(new EmailEnvelope(
                user.Email,
                EmailTemplateName.AdminAccountLockedNotification,
                new Dictionary<string, object>
                {
                    ["UserName"] = user.UserName ?? "Admin",
                    ["Message"] = "Your account has been locked by an administrator."
                },
                correlationId), ct);
        }

        logger.LogInformation("Account {TargetUserId} locked by {ActorId}.", request.TargetUserId, request.ActorId);
        return SelfServiceResult.Ok(request.TargetUserId);
    }
}

// ─── Unlock Account ─────────────────────────────────────────────────────────

public sealed class UnlockAccountHandler(
    UserManager<IdentityUser> userManager,
    IAdminAccountRepository accountRepo,
    IRbacService rbacService,
    IEmailDispatchService emailDispatch,
    IAuditChannelWriter auditWriter,
    ILogger<UnlockAccountHandler> logger) : IRequestHandler<UnlockAccountCommand, SelfServiceResult>
{
    public async Task<SelfServiceResult> Handle(UnlockAccountCommand request, CancellationToken ct)
    {
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");
        var authorizationFailure = await RbacCommandAuthorization.EnsureAsync(rbacService, request.ActorId, PermissionCatalog.UsersActivate, ct);
        if (authorizationFailure is not null)
            return authorizationFailure;

        var user = await userManager.FindByIdAsync(request.TargetUserId);
        if (user is null)
            return SelfServiceResult.Fail("USER_NOT_FOUND", "User not found.");

        var currentState = await accountRepo.GetAccountStateAsync(request.TargetUserId, ct);
        if (currentState is not AdminAccountState.Locked)
            return SelfServiceResult.Fail("NOT_LOCKED", "Account is not locked.");

        await accountRepo.SetAccountStateAsync(request.TargetUserId, AdminAccountState.Active, ct);
        await rbacService.InvalidatePermissionSnapshotAsync(ct);

        auditWriter.TryWrite(AuditRecord.Create(
            AuditAction.AdminAccountUnlocked, request.ActorId, request.TargetUserId, correlationId,
            request.IpAddress, request.UserAgent, AuditOutcome.Success));

        if (user.Email is not null)
        {
            await emailDispatch.EnqueueAsync(new EmailEnvelope(
                user.Email,
                EmailTemplateName.AdminAccountUnlockedNotification,
                new Dictionary<string, object>
                {
                    ["UserName"] = user.UserName ?? "Admin",
                    ["Message"] = "Your account has been unlocked. You can now log in."
                },
                correlationId), ct);
        }

        logger.LogInformation("Account {TargetUserId} unlocked by {ActorId}.", request.TargetUserId, request.ActorId);
        return SelfServiceResult.Ok(request.TargetUserId);
    }
}

// ─── RBAC Query Handlers ────────────────────────────────────────────────────

public sealed class GetAdminProfileDetailHandler(
    IRbacService rbacService) : IRequestHandler<GetAdminProfileDetailQuery, AdminProfileDetailResponse?>
{
    public Task<AdminProfileDetailResponse?> Handle(GetAdminProfileDetailQuery request, CancellationToken ct)
        => rbacService.GetProfileDetailAsync(request.UserId, ct);
}

public sealed class ListAdminUsersHandler(
    IRbacService rbacService) : IRequestHandler<ListAdminUsersQuery, PaginatedResult<AdminUserListItemDto>>
{
    public Task<PaginatedResult<AdminUserListItemDto>> Handle(ListAdminUsersQuery request, CancellationToken ct)
        => rbacService.ListUsersAsync(request.Query, request.Filter, ct);
}

public sealed class GetAdminUserDetailHandler(
    IRbacService rbacService) : IRequestHandler<GetAdminUserDetailQuery, AdminUserDetailDto?>
{
    public Task<AdminUserDetailDto?> Handle(GetAdminUserDetailQuery request, CancellationToken ct)
        => rbacService.GetUserDetailAsync(request.UserId, ct);
}

public sealed class ListRolesHandler(
    IRbacService rbacService) : IRequestHandler<ListRolesQuery, PaginatedResult<RoleListItemDto>>
{
    public Task<PaginatedResult<RoleListItemDto>> Handle(ListRolesQuery request, CancellationToken ct)
        => rbacService.ListRolesAsync(request.Query, request.Filter, ct);
}

public sealed class GetRoleDetailHandler(
    IRbacService rbacService) : IRequestHandler<GetRoleDetailQuery, RoleDetailDto?>
{
    public Task<RoleDetailDto?> Handle(GetRoleDetailQuery request, CancellationToken ct)
        => rbacService.GetRoleDetailAsync(request.RoleId, ct);
}

public sealed class ListPermissionGroupsHandler(
    IRbacService rbacService) : IRequestHandler<ListPermissionGroupsQuery, PaginatedResult<PermissionGroupListItemDto>>
{
    public Task<PaginatedResult<PermissionGroupListItemDto>> Handle(ListPermissionGroupsQuery request, CancellationToken ct)
        => rbacService.ListPermissionGroupsAsync(request.Query, request.Filter, ct);
}

public sealed class GetPermissionGroupDetailHandler(
    IRbacService rbacService) : IRequestHandler<GetPermissionGroupDetailQuery, PermissionGroupDetailDto?>
{
    public Task<PermissionGroupDetailDto?> Handle(GetPermissionGroupDetailQuery request, CancellationToken ct)
        => rbacService.GetPermissionGroupDetailAsync(request.GroupId, ct);
}

public sealed class ListPermissionsHandler(
    IRbacService rbacService) : IRequestHandler<ListPermissionsQuery, IReadOnlyList<PermissionDefinitionDto>>
{
    public Task<IReadOnlyList<PermissionDefinitionDto>> Handle(ListPermissionsQuery request, CancellationToken ct)
        => rbacService.ListPermissionsAsync(ct);
}

// ─── Role Management Handlers ───────────────────────────────────────────────

public sealed class CreateRoleHandler(
    IRbacService rbacService,
    IAuditChannelWriter auditWriter,
    ILogger<CreateRoleHandler> logger) : IRequestHandler<CreateRoleCommand, SelfServiceResult>
{
    public async Task<SelfServiceResult> Handle(CreateRoleCommand request, CancellationToken ct)
    {
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");
        var authorizationFailure = await RbacCommandAuthorization.EnsureAsync(rbacService, request.ActorId, PermissionCatalog.RolesCreate, ct);
        if (authorizationFailure is not null)
            return authorizationFailure;
        string roleId;
        try
        {
            roleId = await rbacService.CreateRoleAsync(request.Name, request.Description, request.Priority, ct);
        }
        catch (InvalidOperationException exception)
        {
            return SelfServiceResult.Fail("ROLE_CREATE_FAILED", exception.Message);
        }

        auditWriter.TryWrite(AuditRecord.Create(
            AuditAction.AdminRoleCreated, request.ActorId, null, correlationId,
            request.IpAddress, request.UserAgent, AuditOutcome.Success, null,
            System.Text.Json.JsonSerializer.Serialize(new { roleId, request.Name })));

        logger.LogInformation("Role '{RoleName}' created by {ActorId}.", request.Name, request.ActorId);
        return SelfServiceResult.Ok(roleId);
    }
}

public sealed class UpdateRoleHandler(
    IRbacService rbacService,
    IAuditChannelWriter auditWriter,
    ILogger<UpdateRoleHandler> logger) : IRequestHandler<UpdateRoleCommand, SelfServiceResult>
{
    public async Task<SelfServiceResult> Handle(UpdateRoleCommand request, CancellationToken ct)
    {
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");
        var authorizationFailure = await RbacCommandAuthorization.EnsureAsync(rbacService, request.ActorId, PermissionCatalog.RolesEdit, ct);
        if (authorizationFailure is not null)
            return authorizationFailure;
        var existingRole = await rbacService.GetRoleDetailAsync(request.RoleId, ct);
        try
        {
            await rbacService.UpdateRoleAsync(request.RoleId, request.Description, request.Priority, ct);
        }
        catch (InvalidOperationException exception)
        {
            return SelfServiceResult.Fail("ROLE_UPDATE_FAILED", exception.Message);
        }

        auditWriter.TryWrite(AuditRecord.Create(
            AuditAction.AdminRoleUpdated, request.ActorId, null, correlationId,
            request.IpAddress, request.UserAgent, AuditOutcome.Success, null,
            JsonSerializer.Serialize(new
            {
                request.RoleId,
                previousDescription = existingRole?.Description,
                newDescription = request.Description,
                previousPriority = existingRole?.Priority,
                newPriority = request.Priority
            })));

        logger.LogInformation("Role {RoleId} updated by {ActorId}.", request.RoleId, request.ActorId);
        return SelfServiceResult.Ok(request.RoleId);
    }
}

public sealed class DeleteRoleHandler(
    IRbacService rbacService,
    IAuditChannelWriter auditWriter,
    ILogger<DeleteRoleHandler> logger) : IRequestHandler<DeleteRoleCommand, SelfServiceResult>
{
    public async Task<SelfServiceResult> Handle(DeleteRoleCommand request, CancellationToken ct)
    {
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");
        var authorizationFailure = await RbacCommandAuthorization.EnsureAsync(rbacService, request.ActorId, PermissionCatalog.RolesDelete, ct);
        if (authorizationFailure is not null)
            return authorizationFailure;
        var existingRole = await rbacService.GetRoleDetailAsync(request.RoleId, ct);
        try
        {
            await rbacService.DeleteRoleAsync(request.RoleId, ct);
        }
        catch (InvalidOperationException exception)
        {
            return SelfServiceResult.Fail("ROLE_DELETE_FAILED", exception.Message);
        }

        auditWriter.TryWrite(AuditRecord.Create(
            AuditAction.AdminRoleDeleted, request.ActorId, null, correlationId,
            request.IpAddress, request.UserAgent, AuditOutcome.Success, null,
            JsonSerializer.Serialize(new
            {
                request.RoleId,
                roleName = existingRole?.Name,
                description = existingRole?.Description,
                directPermissionKeys = existingRole?.DirectPermissionKeys,
                effectivePermissionKeys = existingRole?.EffectivePermissionKeys
            })));

        logger.LogInformation("Role {RoleId} deleted by {ActorId}.", request.RoleId, request.ActorId);
        return SelfServiceResult.Ok(request.RoleId);
    }
}

public sealed class AssignRolePermissionGroupsHandler(
    IRbacService rbacService,
    IAuditChannelWriter auditWriter) : IRequestHandler<AssignRolePermissionGroupsCommand, SelfServiceResult>
{
    public async Task<SelfServiceResult> Handle(AssignRolePermissionGroupsCommand request, CancellationToken ct)
    {
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");
        var authorizationFailure = await RbacCommandAuthorization.EnsureAsync(rbacService, request.ActorId, PermissionCatalog.RolesAssignPermissions, ct);
        if (authorizationFailure is not null)
            return authorizationFailure;
        var existingRole = await rbacService.GetRoleDetailAsync(request.RoleId, ct);
        try
        {
            await rbacService.AssignRolePermissionGroupsAsync(request.RoleId, request.PermissionGroupIds, ct);
        }
        catch (InvalidOperationException exception)
        {
            return SelfServiceResult.Fail("ROLE_ASSIGN_GROUPS_FAILED", exception.Message);
        }

        auditWriter.TryWrite(AuditRecord.Create(
            AuditAction.AdminRoleUpdated, request.ActorId, null, correlationId,
            request.IpAddress, request.UserAgent, AuditOutcome.Success, null,
            JsonSerializer.Serialize(new
            {
                request.RoleId,
                previousPermissionGroupIds = existingRole?.PermissionGroupIds,
                newPermissionGroupIds = request.PermissionGroupIds
            })));

        return SelfServiceResult.Ok(request.RoleId);
    }
}

public sealed class AssignRoleDirectPermissionsHandler(
    IRbacService rbacService,
    IAuditChannelWriter auditWriter) : IRequestHandler<AssignRoleDirectPermissionsCommand, SelfServiceResult>
{
    public async Task<SelfServiceResult> Handle(AssignRoleDirectPermissionsCommand request, CancellationToken ct)
    {
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");
        var authorizationFailure = await RbacCommandAuthorization.EnsureAsync(rbacService, request.ActorId, PermissionCatalog.RolesAssignPermissions, ct);
        if (authorizationFailure is not null)
            return authorizationFailure;
        var existingRole = await rbacService.GetRoleDetailAsync(request.RoleId, ct);
        try
        {
            await rbacService.AssignRoleDirectPermissionsAsync(request.RoleId, request.PermissionKeys, ct);
        }
        catch (InvalidOperationException exception)
        {
            return SelfServiceResult.Fail("ROLE_ASSIGN_PERMISSIONS_FAILED", exception.Message);
        }

        auditWriter.TryWrite(AuditRecord.Create(
            AuditAction.AdminRoleUpdated, request.ActorId, null, correlationId,
            request.IpAddress, request.UserAgent, AuditOutcome.Success, null,
            JsonSerializer.Serialize(new
            {
                request.RoleId,
                previousPermissionKeys = existingRole?.DirectPermissionKeys,
                newPermissionKeys = request.PermissionKeys
            })));

        return SelfServiceResult.Ok(request.RoleId);
    }
}

// ─── Permission Group Management Handlers ───────────────────────────────────

public sealed class CreatePermissionGroupHandler(
    IRbacService rbacService,
    IAuditChannelWriter auditWriter,
    ILogger<CreatePermissionGroupHandler> logger) : IRequestHandler<CreatePermissionGroupCommand, SelfServiceResult>
{
    public async Task<SelfServiceResult> Handle(CreatePermissionGroupCommand request, CancellationToken ct)
    {
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");
        var authorizationFailure = await RbacCommandAuthorization.EnsureAsync(rbacService, request.ActorId, PermissionCatalog.PermissionGroupsCreate, ct);
        if (authorizationFailure is not null)
            return authorizationFailure;
        Guid groupId;
        try
        {
            groupId = await rbacService.CreatePermissionGroupAsync(request.Name, request.Description, ct);
        }
        catch (InvalidOperationException exception)
        {
            return SelfServiceResult.Fail("PERMISSION_GROUP_CREATE_FAILED", exception.Message);
        }

        auditWriter.TryWrite(AuditRecord.Create(
            AuditAction.AdminPermissionGroupCreated, request.ActorId, null, correlationId,
            request.IpAddress, request.UserAgent, AuditOutcome.Success, null,
            System.Text.Json.JsonSerializer.Serialize(new { groupId, request.Name })));

        logger.LogInformation("Permission group '{GroupName}' created by {ActorId}.", request.Name, request.ActorId);
        return SelfServiceResult.Ok(groupId.ToString());
    }
}

public sealed class UpdatePermissionGroupHandler(
    IRbacService rbacService,
    IAuditChannelWriter auditWriter) : IRequestHandler<UpdatePermissionGroupCommand, SelfServiceResult>
{
    public async Task<SelfServiceResult> Handle(UpdatePermissionGroupCommand request, CancellationToken ct)
    {
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");
        var authorizationFailure = await RbacCommandAuthorization.EnsureAsync(rbacService, request.ActorId, PermissionCatalog.PermissionGroupsEdit, ct);
        if (authorizationFailure is not null)
            return authorizationFailure;
        var existingGroup = await rbacService.GetPermissionGroupDetailAsync(request.GroupId, ct);
        try
        {
            await rbacService.UpdatePermissionGroupAsync(request.GroupId, request.Name, request.Description, ct);
        }
        catch (InvalidOperationException exception)
        {
            return SelfServiceResult.Fail("PERMISSION_GROUP_UPDATE_FAILED", exception.Message);
        }

        auditWriter.TryWrite(AuditRecord.Create(
            AuditAction.AdminPermissionGroupUpdated, request.ActorId, null, correlationId,
            request.IpAddress, request.UserAgent, AuditOutcome.Success, null,
            JsonSerializer.Serialize(new
            {
                request.GroupId,
                previousName = existingGroup?.Name,
                newName = request.Name,
                previousDescription = existingGroup?.Description,
                newDescription = request.Description
            })));

        return SelfServiceResult.Ok(request.GroupId.ToString());
    }
}

public sealed class DeletePermissionGroupHandler(
    IRbacService rbacService,
    IAuditChannelWriter auditWriter,
    ILogger<DeletePermissionGroupHandler> logger) : IRequestHandler<DeletePermissionGroupCommand, SelfServiceResult>
{
    public async Task<SelfServiceResult> Handle(DeletePermissionGroupCommand request, CancellationToken ct)
    {
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");
        var authorizationFailure = await RbacCommandAuthorization.EnsureAsync(rbacService, request.ActorId, PermissionCatalog.PermissionGroupsDelete, ct);
        if (authorizationFailure is not null)
            return authorizationFailure;
        var existingGroup = await rbacService.GetPermissionGroupDetailAsync(request.GroupId, ct);
        try
        {
            await rbacService.DeletePermissionGroupAsync(request.GroupId, ct);
        }
        catch (InvalidOperationException exception)
        {
            return SelfServiceResult.Fail("PERMISSION_GROUP_DELETE_FAILED", exception.Message);
        }

        auditWriter.TryWrite(AuditRecord.Create(
            AuditAction.AdminPermissionGroupDeleted, request.ActorId, null, correlationId,
            request.IpAddress, request.UserAgent, AuditOutcome.Success, null,
            JsonSerializer.Serialize(new
            {
                request.GroupId,
                groupName = existingGroup?.Name,
                permissionKeys = existingGroup?.PermissionKeys
            })));

        logger.LogInformation("Permission group {GroupId} deleted by {ActorId}.", request.GroupId, request.ActorId);
        return SelfServiceResult.Ok(request.GroupId.ToString());
    }
}

public sealed class AssignGroupPermissionsHandler(
    IRbacService rbacService,
    IAuditChannelWriter auditWriter) : IRequestHandler<AssignGroupPermissionsCommand, SelfServiceResult>
{
    public async Task<SelfServiceResult> Handle(AssignGroupPermissionsCommand request, CancellationToken ct)
    {
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");
        var authorizationFailure = await RbacCommandAuthorization.EnsureAsync(rbacService, request.ActorId, PermissionCatalog.PermissionGroupsEdit, ct);
        if (authorizationFailure is not null)
            return authorizationFailure;
        var existingGroup = await rbacService.GetPermissionGroupDetailAsync(request.GroupId, ct);
        try
        {
            await rbacService.AssignGroupPermissionsAsync(request.GroupId, request.PermissionKeys, ct);
        }
        catch (InvalidOperationException exception)
        {
            return SelfServiceResult.Fail("PERMISSION_GROUP_ASSIGN_PERMISSIONS_FAILED", exception.Message);
        }

        auditWriter.TryWrite(AuditRecord.Create(
            AuditAction.AdminPermissionGroupUpdated, request.ActorId, null, correlationId,
            request.IpAddress, request.UserAgent, AuditOutcome.Success, null,
            JsonSerializer.Serialize(new
            {
                request.GroupId,
                previousPermissionKeys = existingGroup?.PermissionKeys,
                newPermissionKeys = request.PermissionKeys
            })));

        return SelfServiceResult.Ok(request.GroupId.ToString());
    }
}

internal static class RbacCommandAuthorization
{
    public static async Task<SelfServiceResult?> EnsureAsync(IRbacService rbacService, string actorId, string permissionKey, CancellationToken ct)
    {
        if (await rbacService.IsSuperAdminAsync(actorId, ct))
        {
            return null;
        }

        return await rbacService.UserHasPermissionAsync(actorId, permissionKey, ct)
            ? null
            : SelfServiceResult.Fail("INSUFFICIENT_PERMISSIONS", $"The current admin account requires '{permissionKey}' to perform this action.");
    }
}
