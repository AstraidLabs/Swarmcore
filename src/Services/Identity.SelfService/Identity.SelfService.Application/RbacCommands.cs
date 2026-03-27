using BeeTracker.Contracts.Identity;
using BeeTracker.BuildingBlocks.Application.Queries;
using MediatR;

namespace Identity.SelfService.Application;

// ─── Profile Commands ───────────────────────────────────────────────────────

public sealed record UpdateProfileCommand(
    string UserId,
    string DisplayName,
    string TimeZone,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId) : IRequest<SelfServiceResult>;

public sealed record ChangeEmailCommand(
    string UserId,
    string NewEmail,
    string CurrentPassword,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId) : IRequest<SelfServiceResult>;

// ─── Admin User Management Commands ─────────────────────────────────────────

public sealed record CreateAdminUserCommand(
    string ActorId,
    string UserName,
    string Email,
    string Password,
    string DisplayName,
    IReadOnlyList<string> Roles,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId) : IRequest<SelfServiceResult>;

public sealed record UpdateAdminUserCommand(
    string ActorId,
    string TargetUserId,
    string DisplayName,
    string Email,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId) : IRequest<SelfServiceResult>;

public sealed record AssignRolesCommand(
    string ActorId,
    string TargetUserId,
    IReadOnlyList<string> Roles,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId) : IRequest<SelfServiceResult>;

public sealed record ResetPasswordAdminCommand(
    string ActorId,
    string TargetUserId,
    string NewPassword,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId) : IRequest<SelfServiceResult>;

public sealed record ActivateAccountAdminCommand(
    string ActorId,
    string TargetUserId,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId) : IRequest<SelfServiceResult>;

public sealed record DeactivateAccountAdminCommand(
    string ActorId,
    string TargetUserId,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId) : IRequest<SelfServiceResult>;

public sealed record LockAccountCommand(
    string ActorId,
    string TargetUserId,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId) : IRequest<SelfServiceResult>;

public sealed record UnlockAccountCommand(
    string ActorId,
    string TargetUserId,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId) : IRequest<SelfServiceResult>;

// ─── RBAC Queries ───────────────────────────────────────────────────────────

public sealed record GetAdminProfileDetailQuery(string UserId) : IRequest<AdminProfileDetailResponse?>;

public sealed record ListAdminUsersQuery(GridQuery Query) : IRequest<PaginatedResult<AdminUserListItemDto>>;

public sealed record GetAdminUserDetailQuery(string UserId) : IRequest<AdminUserDetailDto?>;

public sealed record ListRolesQuery(GridQuery Query) : IRequest<PaginatedResult<RoleListItemDto>>;

public sealed record GetRoleDetailQuery(string RoleId) : IRequest<RoleDetailDto?>;

public sealed record ListPermissionGroupsQuery(GridQuery Query) : IRequest<PaginatedResult<PermissionGroupListItemDto>>;

public sealed record GetPermissionGroupDetailQuery(Guid GroupId) : IRequest<PermissionGroupDetailDto?>;

public sealed record ListPermissionsQuery() : IRequest<IReadOnlyList<PermissionDefinitionDto>>;

// ─── Role Management Commands ───────────────────────────────────────────────

public sealed record CreateRoleCommand(
    string ActorId,
    string Name,
    string Description,
    int Priority,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId) : IRequest<SelfServiceResult>;

public sealed record UpdateRoleCommand(
    string ActorId,
    string RoleId,
    string Description,
    int Priority,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId) : IRequest<SelfServiceResult>;

public sealed record DeleteRoleCommand(
    string ActorId,
    string RoleId,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId) : IRequest<SelfServiceResult>;

public sealed record AssignRolePermissionGroupsCommand(
    string ActorId,
    string RoleId,
    IReadOnlyList<Guid> PermissionGroupIds,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId) : IRequest<SelfServiceResult>;

public sealed record AssignRoleDirectPermissionsCommand(
    string ActorId,
    string RoleId,
    IReadOnlyList<string> PermissionKeys,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId) : IRequest<SelfServiceResult>;

// ─── Permission Group Management Commands ───────────────────────────────────

public sealed record CreatePermissionGroupCommand(
    string ActorId,
    string Name,
    string Description,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId) : IRequest<SelfServiceResult>;

public sealed record UpdatePermissionGroupCommand(
    string ActorId,
    Guid GroupId,
    string Name,
    string Description,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId) : IRequest<SelfServiceResult>;

public sealed record DeletePermissionGroupCommand(
    string ActorId,
    Guid GroupId,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId) : IRequest<SelfServiceResult>;

public sealed record AssignGroupPermissionsCommand(
    string ActorId,
    Guid GroupId,
    IReadOnlyList<string> PermissionKeys,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId) : IRequest<SelfServiceResult>;
