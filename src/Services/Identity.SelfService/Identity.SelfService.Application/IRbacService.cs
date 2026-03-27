using BeeTracker.Contracts.Identity;
using BeeTracker.BuildingBlocks.Application.Queries;

namespace Identity.SelfService.Application;

public interface IRbacService
{
    // ─── Permission Resolution ──────────────────────────────────────────────
    Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(string userId, CancellationToken ct);
    Task<bool> UserHasPermissionAsync(string userId, string permissionKey, CancellationToken ct);
    Task<bool> IsSuperAdminAsync(string userId, CancellationToken ct);
    Task<long> GetPermissionSnapshotVersionAsync(CancellationToken ct);
    Task InvalidatePermissionSnapshotAsync(CancellationToken ct);

    // ─── Admin User Management ──────────────────────────────────────────────
    Task<PaginatedResult<AdminUserListItemDto>> ListUsersAsync(GridQuery query, CancellationToken ct);
    Task<AdminUserDetailDto?> GetUserDetailAsync(string userId, CancellationToken ct);
    Task<AdminProfileDetailResponse?> GetProfileDetailAsync(string userId, CancellationToken ct);
    Task UpdateProfileAsync(string userId, string displayName, string timeZone, CancellationToken ct);

    // ─── Role Management ────────────────────────────────────────────────────
    Task<PaginatedResult<RoleListItemDto>> ListRolesAsync(GridQuery query, CancellationToken ct);
    Task<RoleDetailDto?> GetRoleDetailAsync(string roleId, CancellationToken ct);
    Task<string> CreateRoleAsync(string name, string description, int priority, CancellationToken ct);
    Task UpdateRoleAsync(string roleId, string description, int priority, CancellationToken ct);
    Task DeleteRoleAsync(string roleId, CancellationToken ct);
    Task AssignRolePermissionGroupsAsync(string roleId, IReadOnlyList<Guid> permissionGroupIds, CancellationToken ct);
    Task AssignRoleDirectPermissionsAsync(string roleId, IReadOnlyList<string> permissionKeys, CancellationToken ct);

    // ─── Permission Group Management ────────────────────────────────────────
    Task<PaginatedResult<PermissionGroupListItemDto>> ListPermissionGroupsAsync(GridQuery query, CancellationToken ct);
    Task<PermissionGroupDetailDto?> GetPermissionGroupDetailAsync(Guid groupId, CancellationToken ct);
    Task<Guid> CreatePermissionGroupAsync(string name, string description, CancellationToken ct);
    Task UpdatePermissionGroupAsync(Guid groupId, string name, string description, CancellationToken ct);
    Task DeletePermissionGroupAsync(Guid groupId, CancellationToken ct);
    Task AssignGroupPermissionsAsync(Guid groupId, IReadOnlyList<string> permissionKeys, CancellationToken ct);

    // ─── Permission Catalog ─────────────────────────────────────────────────
    Task<IReadOnlyList<PermissionDefinitionDto>> ListPermissionsAsync(CancellationToken ct);

    // ─── Protection Rules ───────────────────────────────────────────────────
    Task<bool> IsLastActiveSuperAdminAsync(string userId, CancellationToken ct);
}
