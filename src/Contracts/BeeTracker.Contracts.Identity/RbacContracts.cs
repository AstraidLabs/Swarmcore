namespace BeeTracker.Contracts.Identity;

// ─── Profile ────────────────────────────────────────────────────────────────

public sealed record UpdateProfileRequest(
    string DisplayName,
    string TimeZone);

public sealed record AdminProfileDetailResponse(
    string UserId,
    string UserName,
    string Email,
    string DisplayName,
    string TimeZone,
    bool IsActive,
    AdminAccountState AccountState,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> EffectivePermissions,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastLoginAtUtc);

// ─── Admin Users ────────────────────────────────────────────────────────────

public sealed record CreateAdminUserRequest(
    string UserName,
    string Email,
    string Password,
    string DisplayName,
    IReadOnlyList<string> Roles);

public sealed record UpdateAdminUserRequest(
    string DisplayName,
    string Email);

public sealed record AssignRolesRequest(
    IReadOnlyList<string> Roles);

public sealed record ResetPasswordAdminRequest(
    string NewPassword);

public sealed record AdminUserListItemDto(
    string UserId,
    string UserName,
    string Email,
    string DisplayName,
    bool IsActive,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastLoginAtUtc);

public sealed record AdminUserDetailDto(
    string UserId,
    string UserName,
    string Email,
    string DisplayName,
    bool IsActive,
    string TimeZone,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> EffectivePermissions,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastLoginAtUtc);

// ─── Roles ──────────────────────────────────────────────────────────────────

public sealed record CreateRoleRequest(
    string Name,
    string Description,
    int Priority);

public sealed record UpdateRoleRequest(
    string Description,
    int Priority);

public sealed record AssignRolePermissionGroupsRequest(
    IReadOnlyList<Guid> PermissionGroupIds);

public sealed record AssignRolePermissionsRequest(
    IReadOnlyList<string> PermissionKeys);

public sealed record RoleListItemDto(
    string RoleId,
    string Name,
    string Description,
    bool IsSystemRole,
    int Priority,
    int UserCount,
    DateTimeOffset CreatedAtUtc);

public sealed record RoleDetailDto(
    string RoleId,
    string Name,
    string Description,
    bool IsSystemRole,
    int Priority,
    IReadOnlyList<Guid> PermissionGroupIds,
    IReadOnlyList<string> DirectPermissionKeys,
    IReadOnlyList<string> EffectivePermissionKeys,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

// ─── Permission Groups ──────────────────────────────────────────────────────

public sealed record CreatePermissionGroupRequest(
    string Name,
    string Description);

public sealed record UpdatePermissionGroupRequest(
    string Name,
    string Description);

public sealed record AssignGroupPermissionsRequest(
    IReadOnlyList<string> PermissionKeys);

public sealed record PermissionGroupListItemDto(
    Guid Id,
    string Name,
    string Description,
    bool IsSystemGroup,
    int PermissionCount,
    DateTimeOffset CreatedAtUtc);

public sealed record PermissionGroupDetailDto(
    Guid Id,
    string Name,
    string Description,
    bool IsSystemGroup,
    IReadOnlyList<string> PermissionKeys,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

// ─── Permission Catalog ─────────────────────────────────────────────────────

public sealed record PermissionDefinitionDto(
    Guid Id,
    string Key,
    string Name,
    string Description,
    string Category,
    bool IsSystemPermission);

// ─── Admin Audit ────────────────────────────────────────────────────────────

public sealed record AdminAuditLogDto(
    Guid Id,
    string Action,
    string ActorId,
    string? TargetEntityType,
    string? TargetEntityId,
    string? BeforeJson,
    string? AfterJson,
    string? IpAddress,
    DateTimeOffset OccurredAtUtc);

// ─── Paginated ──────────────────────────────────────────────────────────────

public sealed record PaginatedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize);
