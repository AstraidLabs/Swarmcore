using System.Security.Claims;
using BeeTracker.Contracts.Identity;
using Identity.SelfService.Application;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Tracker.AdminService.Application;

namespace Tracker.AdminService.Api;

public static class RbacEndpoints
{
    public static RouteGroupBuilder MapRbacEndpoints(this IEndpointRouteBuilder app)
    {
        var rbac = app.MapGroup("/api/admin/rbac").RequireAuthorization();

        // ─── Profile ────────────────────────────────────────────────────
        rbac.MapGet("/profile", HandleGetProfileAsync);
        rbac.MapPut("/profile", HandleUpdateProfileAsync);
        rbac.MapPost("/profile/change-email", HandleChangeEmailAsync);

        // ─── Admin Users ────────────────────────────────────────────────
        rbac.MapGet("/users", HandleListUsersAsync);
        rbac.MapGet("/users/{userId}", HandleGetUserDetailAsync);
        rbac.MapPost("/users", HandleCreateUserAsync);
        rbac.MapPut("/users/{userId}", HandleUpdateUserAsync);
        rbac.MapPut("/users/{userId}/roles", HandleAssignRolesAsync);
        rbac.MapPost("/users/{userId}/reset-password", HandleResetPasswordAsync);
        rbac.MapPost("/users/{userId}/activate", HandleActivateAccountAsync);
        rbac.MapPost("/users/{userId}/deactivate", HandleDeactivateAccountAsync);
        rbac.MapPost("/users/{userId}/lock", HandleLockAccountAsync);
        rbac.MapPost("/users/{userId}/unlock", HandleUnlockAccountAsync);

        // ─── Roles ──────────────────────────────────────────────────────
        rbac.MapGet("/roles", HandleListRolesAsync);
        rbac.MapGet("/roles/{roleId}", HandleGetRoleDetailAsync);
        rbac.MapPost("/roles", HandleCreateRoleAsync);
        rbac.MapPut("/roles/{roleId}", HandleUpdateRoleAsync);
        rbac.MapDelete("/roles/{roleId}", HandleDeleteRoleAsync);
        rbac.MapPut("/roles/{roleId}/permission-groups", HandleAssignRolePermissionGroupsAsync);
        rbac.MapPut("/roles/{roleId}/permissions", HandleAssignRoleDirectPermissionsAsync);

        // ─── Permission Groups ──────────────────────────────────────────
        rbac.MapGet("/permission-groups", HandleListPermissionGroupsAsync);
        rbac.MapGet("/permission-groups/{groupId:guid}", HandleGetPermissionGroupDetailAsync);
        rbac.MapPost("/permission-groups", HandleCreatePermissionGroupAsync);
        rbac.MapPut("/permission-groups/{groupId:guid}", HandleUpdatePermissionGroupAsync);
        rbac.MapDelete("/permission-groups/{groupId:guid}", HandleDeletePermissionGroupAsync);
        rbac.MapPut("/permission-groups/{groupId:guid}/permissions", HandleAssignGroupPermissionsAsync);

        // ─── Permissions Catalog ────────────────────────────────────────
        rbac.MapGet("/permissions", HandleListPermissionsAsync);

        return rbac;
    }

    // ─── Profile ────────────────────────────────────────────────────────────

    private static async Task<IResult> HandleGetProfileAsync(
        HttpContext httpContext,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext);
        if (userId is null) return Results.Unauthorized();

        var profile = await sender.Send(new GetAdminProfileDetailQuery(userId), ct);
        return profile is null ? Results.NotFound() : Results.Ok(profile);
    }

    private static async Task<IResult> HandleUpdateProfileAsync(
        HttpContext httpContext,
        [FromBody] UpdateProfileRequest request,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext);
        if (userId is null) return Results.Unauthorized();

        var result = await sender.Send(new UpdateProfileCommand(
            userId, request.DisplayName, request.TimeZone,
            GetIpAddress(httpContext), GetUserAgent(httpContext), GetCorrelationId(httpContext)), ct);

        return result.Success ? Results.Ok(result) : Results.BadRequest(ToError(result));
    }

    private static async Task<IResult> HandleChangeEmailAsync(
        HttpContext httpContext,
        [FromBody] ChangeEmailRequest request,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext);
        if (userId is null) return Results.Unauthorized();

        var result = await sender.Send(new ChangeEmailCommand(
            userId, request.NewEmail, request.CurrentPassword,
            GetIpAddress(httpContext), GetUserAgent(httpContext), GetCorrelationId(httpContext)), ct);

        return result.Success ? Results.Ok(result) : Results.BadRequest(ToError(result));
    }

    // ─── Admin Users ────────────────────────────────────────────────────────

    private static async Task<IResult> HandleListUsersAsync(
        [FromQuery] string? search,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var result = await sender.Send(new ListAdminUsersQuery(search, page, pageSize), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> HandleGetUserDetailAsync(
        string userId,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var result = await sender.Send(new GetAdminUserDetailQuery(userId), ct);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> HandleCreateUserAsync(
        HttpContext httpContext,
        [FromBody] CreateAdminUserRequest request,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var actorId = GetUserId(httpContext);
        if (actorId is null) return Results.Unauthorized();

        var result = await sender.Send(new CreateAdminUserCommand(
            actorId, request.UserName, request.Email, request.Password, request.DisplayName, request.Roles,
            GetIpAddress(httpContext), GetUserAgent(httpContext), GetCorrelationId(httpContext)), ct);

        return result.Success ? Results.Created($"/api/admin/rbac/users/{result.UserId}", result) : Results.BadRequest(ToError(result));
    }

    private static async Task<IResult> HandleUpdateUserAsync(
        HttpContext httpContext,
        string userId,
        [FromBody] UpdateAdminUserRequest request,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var actorId = GetUserId(httpContext);
        if (actorId is null) return Results.Unauthorized();

        var result = await sender.Send(new UpdateAdminUserCommand(
            actorId, userId, request.DisplayName, request.Email,
            GetIpAddress(httpContext), GetUserAgent(httpContext), GetCorrelationId(httpContext)), ct);

        return result.Success ? Results.Ok(result) : Results.BadRequest(ToError(result));
    }

    private static async Task<IResult> HandleAssignRolesAsync(
        HttpContext httpContext,
        string userId,
        [FromBody] AssignRolesRequest request,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var actorId = GetUserId(httpContext);
        if (actorId is null) return Results.Unauthorized();

        var result = await sender.Send(new AssignRolesCommand(
            actorId, userId, request.Roles,
            GetIpAddress(httpContext), GetUserAgent(httpContext), GetCorrelationId(httpContext)), ct);

        return result.Success ? Results.Ok(result) : Results.BadRequest(ToError(result));
    }

    private static async Task<IResult> HandleResetPasswordAsync(
        HttpContext httpContext,
        string userId,
        [FromBody] ResetPasswordAdminRequest request,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var actorId = GetUserId(httpContext);
        if (actorId is null) return Results.Unauthorized();

        var result = await sender.Send(new ResetPasswordAdminCommand(
            actorId, userId, request.NewPassword,
            GetIpAddress(httpContext), GetUserAgent(httpContext), GetCorrelationId(httpContext)), ct);

        return result.Success ? Results.Ok(result) : Results.BadRequest(ToError(result));
    }

    private static async Task<IResult> HandleActivateAccountAsync(
        HttpContext httpContext,
        string userId,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var actorId = GetUserId(httpContext);
        if (actorId is null) return Results.Unauthorized();

        var result = await sender.Send(new ActivateAccountAdminCommand(
            actorId, userId,
            GetIpAddress(httpContext), GetUserAgent(httpContext), GetCorrelationId(httpContext)), ct);

        return result.Success ? Results.Ok(result) : Results.BadRequest(ToError(result));
    }

    private static async Task<IResult> HandleDeactivateAccountAsync(
        HttpContext httpContext,
        string userId,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var actorId = GetUserId(httpContext);
        if (actorId is null) return Results.Unauthorized();

        var result = await sender.Send(new DeactivateAccountAdminCommand(
            actorId, userId,
            GetIpAddress(httpContext), GetUserAgent(httpContext), GetCorrelationId(httpContext)), ct);

        return result.Success ? Results.Ok(result) : Results.BadRequest(ToError(result));
    }

    private static async Task<IResult> HandleLockAccountAsync(
        HttpContext httpContext,
        string userId,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var actorId = GetUserId(httpContext);
        if (actorId is null) return Results.Unauthorized();

        var result = await sender.Send(new LockAccountCommand(
            actorId, userId,
            GetIpAddress(httpContext), GetUserAgent(httpContext), GetCorrelationId(httpContext)), ct);

        return result.Success ? Results.Ok(result) : Results.BadRequest(ToError(result));
    }

    private static async Task<IResult> HandleUnlockAccountAsync(
        HttpContext httpContext,
        string userId,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var actorId = GetUserId(httpContext);
        if (actorId is null) return Results.Unauthorized();

        var result = await sender.Send(new UnlockAccountCommand(
            actorId, userId,
            GetIpAddress(httpContext), GetUserAgent(httpContext), GetCorrelationId(httpContext)), ct);

        return result.Success ? Results.Ok(result) : Results.BadRequest(ToError(result));
    }

    // ─── Roles ──────────────────────────────────────────────────────────────

    private static async Task<IResult> HandleListRolesAsync(
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var result = await sender.Send(new ListRolesQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> HandleGetRoleDetailAsync(
        string roleId,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var result = await sender.Send(new GetRoleDetailQuery(roleId), ct);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> HandleCreateRoleAsync(
        HttpContext httpContext,
        [FromBody] CreateRoleRequest request,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var actorId = GetUserId(httpContext);
        if (actorId is null) return Results.Unauthorized();

        var result = await sender.Send(new CreateRoleCommand(
            actorId, request.Name, request.Description, request.Priority,
            GetIpAddress(httpContext), GetUserAgent(httpContext), GetCorrelationId(httpContext)), ct);

        return result.Success ? Results.Created($"/api/admin/rbac/roles/{result.UserId}", result) : Results.BadRequest(ToError(result));
    }

    private static async Task<IResult> HandleUpdateRoleAsync(
        HttpContext httpContext,
        string roleId,
        [FromBody] UpdateRoleRequest request,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var actorId = GetUserId(httpContext);
        if (actorId is null) return Results.Unauthorized();

        var result = await sender.Send(new UpdateRoleCommand(
            actorId, roleId, request.Description, request.Priority,
            GetIpAddress(httpContext), GetUserAgent(httpContext), GetCorrelationId(httpContext)), ct);

        return result.Success ? Results.Ok(result) : Results.BadRequest(ToError(result));
    }

    private static async Task<IResult> HandleDeleteRoleAsync(
        HttpContext httpContext,
        string roleId,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var actorId = GetUserId(httpContext);
        if (actorId is null) return Results.Unauthorized();

        var result = await sender.Send(new DeleteRoleCommand(
            actorId, roleId,
            GetIpAddress(httpContext), GetUserAgent(httpContext), GetCorrelationId(httpContext)), ct);

        return result.Success ? Results.NoContent() : Results.BadRequest(ToError(result));
    }

    private static async Task<IResult> HandleAssignRolePermissionGroupsAsync(
        HttpContext httpContext,
        string roleId,
        [FromBody] AssignRolePermissionGroupsRequest request,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var actorId = GetUserId(httpContext);
        if (actorId is null) return Results.Unauthorized();

        var result = await sender.Send(new AssignRolePermissionGroupsCommand(
            actorId, roleId, request.PermissionGroupIds,
            GetIpAddress(httpContext), GetUserAgent(httpContext), GetCorrelationId(httpContext)), ct);

        return result.Success ? Results.Ok(result) : Results.BadRequest(ToError(result));
    }

    private static async Task<IResult> HandleAssignRoleDirectPermissionsAsync(
        HttpContext httpContext,
        string roleId,
        [FromBody] AssignRolePermissionsRequest request,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var actorId = GetUserId(httpContext);
        if (actorId is null) return Results.Unauthorized();

        var result = await sender.Send(new AssignRoleDirectPermissionsCommand(
            actorId, roleId, request.PermissionKeys,
            GetIpAddress(httpContext), GetUserAgent(httpContext), GetCorrelationId(httpContext)), ct);

        return result.Success ? Results.Ok(result) : Results.BadRequest(ToError(result));
    }

    // ─── Permission Groups ──────────────────────────────────────────────────

    private static async Task<IResult> HandleListPermissionGroupsAsync(
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var result = await sender.Send(new ListPermissionGroupsQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> HandleGetPermissionGroupDetailAsync(
        Guid groupId,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var result = await sender.Send(new GetPermissionGroupDetailQuery(groupId), ct);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> HandleCreatePermissionGroupAsync(
        HttpContext httpContext,
        [FromBody] CreatePermissionGroupRequest request,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var actorId = GetUserId(httpContext);
        if (actorId is null) return Results.Unauthorized();

        var result = await sender.Send(new CreatePermissionGroupCommand(
            actorId, request.Name, request.Description,
            GetIpAddress(httpContext), GetUserAgent(httpContext), GetCorrelationId(httpContext)), ct);

        return result.Success ? Results.Created($"/api/admin/rbac/permission-groups/{result.UserId}", result) : Results.BadRequest(ToError(result));
    }

    private static async Task<IResult> HandleUpdatePermissionGroupAsync(
        HttpContext httpContext,
        Guid groupId,
        [FromBody] UpdatePermissionGroupRequest request,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var actorId = GetUserId(httpContext);
        if (actorId is null) return Results.Unauthorized();

        var result = await sender.Send(new UpdatePermissionGroupCommand(
            actorId, groupId, request.Name, request.Description,
            GetIpAddress(httpContext), GetUserAgent(httpContext), GetCorrelationId(httpContext)), ct);

        return result.Success ? Results.Ok(result) : Results.BadRequest(ToError(result));
    }

    private static async Task<IResult> HandleDeletePermissionGroupAsync(
        HttpContext httpContext,
        Guid groupId,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var actorId = GetUserId(httpContext);
        if (actorId is null) return Results.Unauthorized();

        var result = await sender.Send(new DeletePermissionGroupCommand(
            actorId, groupId,
            GetIpAddress(httpContext), GetUserAgent(httpContext), GetCorrelationId(httpContext)), ct);

        return result.Success ? Results.NoContent() : Results.BadRequest(ToError(result));
    }

    private static async Task<IResult> HandleAssignGroupPermissionsAsync(
        HttpContext httpContext,
        Guid groupId,
        [FromBody] AssignGroupPermissionsRequest request,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var actorId = GetUserId(httpContext);
        if (actorId is null) return Results.Unauthorized();

        var result = await sender.Send(new AssignGroupPermissionsCommand(
            actorId, groupId, request.PermissionKeys,
            GetIpAddress(httpContext), GetUserAgent(httpContext), GetCorrelationId(httpContext)), ct);

        return result.Success ? Results.Ok(result) : Results.BadRequest(ToError(result));
    }

    // ─── Permissions Catalog ────────────────────────────────────────────────

    private static async Task<IResult> HandleListPermissionsAsync(
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var result = await sender.Send(new ListPermissionsQuery(), ct);
        return Results.Ok(result);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static string? GetUserId(HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

    private static string? GetIpAddress(HttpContext httpContext)
        => httpContext.Connection.RemoteIpAddress?.ToString();

    private static string? GetUserAgent(HttpContext httpContext)
        => httpContext.Request.Headers.UserAgent.FirstOrDefault();

    private static string GetCorrelationId(HttpContext httpContext)
        => httpContext.TraceIdentifier;

    private static SelfServiceErrorResponse ToError(SelfServiceResult result)
        => new(result.ErrorCode ?? "UNKNOWN", "Operation failed.", result.ErrorMessages);
}

// ─── Request DTOs ───────────────────────────────────────────────────────────

public sealed record ChangeEmailRequest(string NewEmail, string CurrentPassword);
