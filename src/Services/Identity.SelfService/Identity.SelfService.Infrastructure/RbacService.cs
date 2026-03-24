using BeeTracker.Contracts.Identity;
using Identity.SelfService.Application;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using DomainAccountState = Identity.SelfService.Domain.AdminAccountState;
using DomainPermissionCatalog = Identity.SelfService.Domain.PermissionCatalog;
using DomainSystemRoleNames = Identity.SelfService.Domain.SystemRoleNames;

namespace Identity.SelfService.Infrastructure;

public sealed class RbacService(
    SelfServiceDbContext db,
    UserManager<IdentityUser> userManager,
    RoleManager<IdentityRole> roleManager) : IRbacService
{
    // ─── Permission Resolution ──────────────────────────────────────────────

    public async Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(string userId, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
            return new HashSet<string>();

        var roles = await userManager.GetRolesAsync(user);

        // SuperAdmin gets all permissions
        if (roles.Contains(DomainSystemRoleNames.SuperAdmin, StringComparer.OrdinalIgnoreCase))
        {
            return DomainPermissionCatalog.All.Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var roleIds = new List<string>();
        foreach (var roleName in roles)
        {
            var role = await roleManager.FindByNameAsync(roleName);
            if (role is not null)
                roleIds.Add(role.Id);
        }

        if (roleIds.Count == 0)
            return new HashSet<string>();

        // Direct role permissions
        var directPermissionIds = await db.RolePermissions
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => rp.PermissionDefinitionId)
            .ToListAsync(ct);

        // Permissions from groups assigned to roles
        var groupIds = await db.RolePermissionGroups
            .Where(rpg => roleIds.Contains(rpg.RoleId))
            .Select(rpg => rpg.PermissionGroupId)
            .ToListAsync(ct);

        var groupPermissionIds = groupIds.Count > 0
            ? await db.PermissionGroupItems
                .Where(pgi => groupIds.Contains(pgi.PermissionGroupId))
                .Select(pgi => pgi.PermissionDefinitionId)
                .ToListAsync(ct)
            : [];

        var allPermissionIds = directPermissionIds.Union(groupPermissionIds).Distinct().ToList();

        if (allPermissionIds.Count == 0)
            return new HashSet<string>();

        var permissionKeys = await db.PermissionDefinitions
            .Where(pd => allPermissionIds.Contains(pd.Id))
            .Select(pd => pd.Key)
            .ToListAsync(ct);

        return permissionKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<bool> UserHasPermissionAsync(string userId, string permissionKey, CancellationToken ct)
    {
        var permissions = await GetEffectivePermissionsAsync(userId, ct);
        return permissions.Contains(permissionKey);
    }

    public async Task<bool> IsSuperAdminAsync(string userId, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return false;
        var roles = await userManager.GetRolesAsync(user);
        return roles.Contains(DomainSystemRoleNames.SuperAdmin, StringComparer.OrdinalIgnoreCase);
    }

    // ─── Admin User Management ──────────────────────────────────────────────

    public async Task<PaginatedResult<AdminUserListItemDto>> ListUsersAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = userManager.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(u => u.NormalizedUserName!.Contains(term) || u.NormalizedEmail!.Contains(term));
        }

        var totalCount = await query.CountAsync(ct);
        var users = await query
            .OrderBy(u => u.UserName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = new List<AdminUserListItemDto>();
        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            var profile = await db.AdminUserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == user.Id, ct);
            var accountState = await db.AdminAccountStates.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == user.Id, ct);

            items.Add(new AdminUserListItemDto(
                user.Id,
                user.UserName ?? string.Empty,
                user.Email ?? string.Empty,
                profile?.DisplayName ?? user.UserName ?? string.Empty,
                accountState?.State == DomainAccountState.Active.ToString(),
                roles.ToList().AsReadOnly(),
                accountState?.CreatedAtUtc ?? DateTimeOffset.MinValue,
                accountState?.LastLoginAtUtc));
        }

        return new PaginatedResult<AdminUserListItemDto>(items, totalCount, page, pageSize);
    }

    public async Task<AdminUserDetailDto?> GetUserDetailAsync(string userId, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return null;

        var roles = await userManager.GetRolesAsync(user);
        var profile = await db.AdminUserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var accountState = await db.AdminAccountStates.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == userId, ct);
        var effectivePermissions = await GetEffectivePermissionsAsync(userId, ct);

        return new AdminUserDetailDto(
            user.Id,
            user.UserName ?? string.Empty,
            user.Email ?? string.Empty,
            profile?.DisplayName ?? user.UserName ?? string.Empty,
            accountState?.State == DomainAccountState.Active.ToString(),
            profile?.TimeZone ?? "UTC",
            roles.ToList().AsReadOnly(),
            effectivePermissions.OrderBy(p => p).ToList().AsReadOnly(),
            accountState?.CreatedAtUtc ?? DateTimeOffset.MinValue,
            accountState?.LastLoginAtUtc);
    }

    public async Task<AdminProfileDetailResponse?> GetProfileDetailAsync(string userId, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return null;

        var roles = await userManager.GetRolesAsync(user);
        var profile = await db.AdminUserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var accountState = await db.AdminAccountStates.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == userId, ct);
        var effectivePermissions = await GetEffectivePermissionsAsync(userId, ct);

        var state = accountState is not null && Enum.TryParse<DomainAccountState>(accountState.State, true, out var parsed)
            ? (AdminAccountState)(int)parsed
            : AdminAccountState.PendingActivation;

        return new AdminProfileDetailResponse(
            user.Id,
            user.UserName ?? string.Empty,
            user.Email ?? string.Empty,
            profile?.DisplayName ?? user.UserName ?? string.Empty,
            profile?.TimeZone ?? "UTC",
            accountState?.State == DomainAccountState.Active.ToString(),
            state,
            roles.ToList().AsReadOnly(),
            effectivePermissions.OrderBy(p => p).ToList().AsReadOnly(),
            accountState?.CreatedAtUtc ?? DateTimeOffset.MinValue,
            accountState?.LastLoginAtUtc);
    }

    public async Task UpdateProfileAsync(string userId, string displayName, string timeZone, CancellationToken ct)
    {
        var profile = await db.AdminUserProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var now = DateTimeOffset.UtcNow;

        if (profile is null)
        {
            profile = new AdminUserProfileEntity
            {
                UserId = userId,
                DisplayName = displayName,
                IsActive = true,
                TimeZone = timeZone,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.AdminUserProfiles.Add(profile);
        }
        else
        {
            profile.DisplayName = displayName;
            profile.TimeZone = timeZone;
            profile.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(ct);
    }

    // ─── Role Management ────────────────────────────────────────────────────

    public async Task<IReadOnlyList<RoleListItemDto>> ListRolesAsync(CancellationToken ct)
    {
        var roles = await roleManager.Roles.AsNoTracking().ToListAsync(ct);
        var items = new List<RoleListItemDto>();

        foreach (var role in roles)
        {
            var metadata = await db.RoleMetadata.AsNoTracking().FirstOrDefaultAsync(m => m.RoleId == role.Id, ct);
            var userCount = (await userManager.GetUsersInRoleAsync(role.Name!)).Count;

            items.Add(new RoleListItemDto(
                role.Id,
                role.Name ?? string.Empty,
                metadata?.Description ?? string.Empty,
                metadata?.IsSystemRole ?? false,
                metadata?.Priority ?? 0,
                userCount,
                metadata?.CreatedAtUtc ?? DateTimeOffset.MinValue));
        }

        return items.OrderByDescending(r => r.Priority).ThenBy(r => r.Name).ToList().AsReadOnly();
    }

    public async Task<RoleDetailDto?> GetRoleDetailAsync(string roleId, CancellationToken ct)
    {
        var role = await roleManager.FindByIdAsync(roleId);
        if (role is null) return null;

        var metadata = await db.RoleMetadata.AsNoTracking().FirstOrDefaultAsync(m => m.RoleId == roleId, ct);

        var permissionGroupIds = await db.RolePermissionGroups
            .Where(rpg => rpg.RoleId == roleId)
            .Select(rpg => rpg.PermissionGroupId)
            .ToListAsync(ct);

        var directPermissionIds = await db.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.PermissionDefinitionId)
            .ToListAsync(ct);

        var directPermissionKeys = directPermissionIds.Count > 0
            ? await db.PermissionDefinitions
                .Where(pd => directPermissionIds.Contains(pd.Id))
                .Select(pd => pd.Key)
                .ToListAsync(ct)
            : new List<string>();

        // Effective = direct + from groups
        var groupPermissionIds = permissionGroupIds.Count > 0
            ? await db.PermissionGroupItems
                .Where(pgi => permissionGroupIds.Contains(pgi.PermissionGroupId))
                .Select(pgi => pgi.PermissionDefinitionId)
                .ToListAsync(ct)
            : new List<Guid>();

        var allPermIds = directPermissionIds.Union(groupPermissionIds).Distinct().ToList();
        var effectiveKeys = allPermIds.Count > 0
            ? await db.PermissionDefinitions
                .Where(pd => allPermIds.Contains(pd.Id))
                .Select(pd => pd.Key)
                .ToListAsync(ct)
            : new List<string>();

        // SuperAdmin gets all
        if (string.Equals(role.Name, DomainSystemRoleNames.SuperAdmin, StringComparison.OrdinalIgnoreCase))
        {
            effectiveKeys = DomainPermissionCatalog.All.Select(p => p.Key).ToList();
        }

        return new RoleDetailDto(
            role.Id,
            role.Name ?? string.Empty,
            metadata?.Description ?? string.Empty,
            metadata?.IsSystemRole ?? false,
            metadata?.Priority ?? 0,
            permissionGroupIds.AsReadOnly(),
            directPermissionKeys.AsReadOnly(),
            effectiveKeys.OrderBy(k => k).ToList().AsReadOnly(),
            metadata?.CreatedAtUtc ?? DateTimeOffset.MinValue,
            metadata?.UpdatedAtUtc ?? DateTimeOffset.MinValue);
    }

    public async Task<string> CreateRoleAsync(string name, string description, int priority, CancellationToken ct)
    {
        if (DomainSystemRoleNames.All.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Cannot create role with system name '{name}'.");

        var existingRole = await roleManager.FindByNameAsync(name);
        if (existingRole is not null)
            throw new InvalidOperationException($"Role '{name}' already exists.");

        var role = new IdentityRole(name);
        var result = await roleManager.CreateAsync(role);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Failed to create role: {string.Join(", ", result.Errors.Select(e => e.Description))}");

        var now = DateTimeOffset.UtcNow;
        db.RoleMetadata.Add(new RoleMetadataEntity
        {
            RoleId = role.Id,
            Description = description,
            IsSystemRole = false,
            Priority = priority,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await db.SaveChangesAsync(ct);

        return role.Id;
    }

    public async Task UpdateRoleAsync(string roleId, string description, int priority, CancellationToken ct)
    {
        var metadata = await db.RoleMetadata.FirstOrDefaultAsync(m => m.RoleId == roleId, ct)
            ?? throw new InvalidOperationException("Role metadata not found.");

        metadata.Description = description;
        metadata.Priority = priority;
        metadata.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteRoleAsync(string roleId, CancellationToken ct)
    {
        var role = await roleManager.FindByIdAsync(roleId)
            ?? throw new InvalidOperationException("Role not found.");

        var metadata = await db.RoleMetadata.FirstOrDefaultAsync(m => m.RoleId == roleId, ct);
        if (metadata?.IsSystemRole == true)
            throw new InvalidOperationException("Cannot delete a system role.");

        // Remove all related data
        var rolePermGroups = await db.RolePermissionGroups.Where(rpg => rpg.RoleId == roleId).ToListAsync(ct);
        db.RolePermissionGroups.RemoveRange(rolePermGroups);

        var rolePerms = await db.RolePermissions.Where(rp => rp.RoleId == roleId).ToListAsync(ct);
        db.RolePermissions.RemoveRange(rolePerms);

        if (metadata is not null)
            db.RoleMetadata.Remove(metadata);

        await db.SaveChangesAsync(ct);

        var deleteResult = await roleManager.DeleteAsync(role);
        if (!deleteResult.Succeeded)
            throw new InvalidOperationException($"Failed to delete role: {string.Join(", ", deleteResult.Errors.Select(e => e.Description))}");
    }

    public async Task AssignRolePermissionGroupsAsync(string roleId, IReadOnlyList<Guid> permissionGroupIds, CancellationToken ct)
    {
        var existing = await db.RolePermissionGroups.Where(rpg => rpg.RoleId == roleId).ToListAsync(ct);
        db.RolePermissionGroups.RemoveRange(existing);

        foreach (var groupId in permissionGroupIds.Distinct())
        {
            db.RolePermissionGroups.Add(new RolePermissionGroupEntity { RoleId = roleId, PermissionGroupId = groupId });
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task AssignRoleDirectPermissionsAsync(string roleId, IReadOnlyList<string> permissionKeys, CancellationToken ct)
    {
        var existing = await db.RolePermissions.Where(rp => rp.RoleId == roleId).ToListAsync(ct);
        db.RolePermissions.RemoveRange(existing);

        var permissionDefinitions = await db.PermissionDefinitions
            .Where(pd => permissionKeys.Contains(pd.Key))
            .ToListAsync(ct);

        foreach (var pd in permissionDefinitions)
        {
            db.RolePermissions.Add(new RolePermissionEntity { RoleId = roleId, PermissionDefinitionId = pd.Id });
        }

        await db.SaveChangesAsync(ct);
    }

    // ─── Permission Group Management ────────────────────────────────────────

    public async Task<IReadOnlyList<PermissionGroupListItemDto>> ListPermissionGroupsAsync(CancellationToken ct)
    {
        var groups = await db.PermissionGroups.AsNoTracking().ToListAsync(ct);
        var items = new List<PermissionGroupListItemDto>();

        foreach (var group in groups)
        {
            var permCount = await db.PermissionGroupItems
                .CountAsync(pgi => pgi.PermissionGroupId == group.Id, ct);

            items.Add(new PermissionGroupListItemDto(
                group.Id,
                group.Name,
                group.Description,
                group.IsSystemGroup,
                permCount,
                group.CreatedAtUtc));
        }

        return items.OrderBy(g => g.Name).ToList().AsReadOnly();
    }

    public async Task<PermissionGroupDetailDto?> GetPermissionGroupDetailAsync(Guid groupId, CancellationToken ct)
    {
        var group = await db.PermissionGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null) return null;

        var permissionIds = await db.PermissionGroupItems
            .Where(pgi => pgi.PermissionGroupId == groupId)
            .Select(pgi => pgi.PermissionDefinitionId)
            .ToListAsync(ct);

        var permissionKeys = permissionIds.Count > 0
            ? await db.PermissionDefinitions
                .Where(pd => permissionIds.Contains(pd.Id))
                .Select(pd => pd.Key)
                .ToListAsync(ct)
            : new List<string>();

        return new PermissionGroupDetailDto(
            group.Id,
            group.Name,
            group.Description,
            group.IsSystemGroup,
            permissionKeys.OrderBy(k => k).ToList().AsReadOnly(),
            group.CreatedAtUtc,
            group.UpdatedAtUtc);
    }

    public async Task<Guid> CreatePermissionGroupAsync(string name, string description, CancellationToken ct)
    {
        var existing = await db.PermissionGroups.AnyAsync(g => g.Name == name, ct);
        if (existing)
            throw new InvalidOperationException($"Permission group '{name}' already exists.");

        var now = DateTimeOffset.UtcNow;
        var entity = new PermissionGroupEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            IsSystemGroup = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.PermissionGroups.Add(entity);
        await db.SaveChangesAsync(ct);

        return entity.Id;
    }

    public async Task UpdatePermissionGroupAsync(Guid groupId, string name, string description, CancellationToken ct)
    {
        var group = await db.PermissionGroups.FirstOrDefaultAsync(g => g.Id == groupId, ct)
            ?? throw new InvalidOperationException("Permission group not found.");

        group.Name = name;
        group.Description = description;
        group.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeletePermissionGroupAsync(Guid groupId, CancellationToken ct)
    {
        var group = await db.PermissionGroups.FirstOrDefaultAsync(g => g.Id == groupId, ct)
            ?? throw new InvalidOperationException("Permission group not found.");

        if (group.IsSystemGroup)
            throw new InvalidOperationException("Cannot delete a system permission group.");

        var items = await db.PermissionGroupItems.Where(pgi => pgi.PermissionGroupId == groupId).ToListAsync(ct);
        db.PermissionGroupItems.RemoveRange(items);

        var roleLinks = await db.RolePermissionGroups.Where(rpg => rpg.PermissionGroupId == groupId).ToListAsync(ct);
        db.RolePermissionGroups.RemoveRange(roleLinks);

        db.PermissionGroups.Remove(group);
        await db.SaveChangesAsync(ct);
    }

    public async Task AssignGroupPermissionsAsync(Guid groupId, IReadOnlyList<string> permissionKeys, CancellationToken ct)
    {
        var existing = await db.PermissionGroupItems.Where(pgi => pgi.PermissionGroupId == groupId).ToListAsync(ct);
        db.PermissionGroupItems.RemoveRange(existing);

        var permissionDefinitions = await db.PermissionDefinitions
            .Where(pd => permissionKeys.Contains(pd.Key))
            .ToListAsync(ct);

        foreach (var pd in permissionDefinitions)
        {
            db.PermissionGroupItems.Add(new PermissionGroupItemEntity
            {
                PermissionGroupId = groupId,
                PermissionDefinitionId = pd.Id
            });
        }

        await db.SaveChangesAsync(ct);
    }

    // ─── Permission Catalog ─────────────────────────────────────────────────

    public async Task<IReadOnlyList<PermissionDefinitionDto>> ListPermissionsAsync(CancellationToken ct)
    {
        var permissions = await db.PermissionDefinitions.AsNoTracking()
            .OrderBy(p => p.Category).ThenBy(p => p.Key)
            .ToListAsync(ct);

        return permissions.Select(p => new PermissionDefinitionDto(
            p.Id, p.Key, p.Name, p.Description, p.Category, p.IsSystemPermission
        )).ToList().AsReadOnly();
    }

    // ─── Protection Rules ───────────────────────────────────────────────────

    public async Task<bool> IsLastActiveSuperAdminAsync(string userId, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return false;

        var userRoles = await userManager.GetRolesAsync(user);
        if (!userRoles.Contains(DomainSystemRoleNames.SuperAdmin, StringComparer.OrdinalIgnoreCase))
            return false;

        // Find all SuperAdmin users
        var superAdminUsers = await userManager.GetUsersInRoleAsync(DomainSystemRoleNames.SuperAdmin);
        var activeSuperAdminCount = 0;

        foreach (var sa in superAdminUsers)
        {
            var state = await db.AdminAccountStates.AsNoTracking()
                .FirstOrDefaultAsync(a => a.UserId == sa.Id, ct);
            if (state?.State == DomainAccountState.Active.ToString())
                activeSuperAdminCount++;
        }

        return activeSuperAdminCount <= 1;
    }
}
