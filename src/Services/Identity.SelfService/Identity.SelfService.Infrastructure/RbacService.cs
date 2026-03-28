using BeeTracker.Contracts.Identity;
using BeeTracker.BuildingBlocks.Application.Queries;
using BeeTracker.BuildingBlocks.Infrastructure.Data;
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
    private const string PermissionSnapshotStateKey = "permission_snapshot";

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

    public async Task<long> GetPermissionSnapshotVersionAsync(CancellationToken ct)
    {
        var state = await EnsurePermissionSnapshotStateAsync(ct);
        return state.Version;
    }

    public async Task InvalidatePermissionSnapshotAsync(CancellationToken ct)
    {
        var state = await EnsurePermissionSnapshotStateAsync(ct);
        state.Version++;
        state.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    // ─── Admin User Management ──────────────────────────────────────────────

    public async Task<PaginatedResult<AdminUserListItemDto>> ListUsersAsync(GridQuery request, AdminUserCatalogFilter filter, CancellationToken ct)
    {
        request = request.Normalize(maxPageSize: 100);
        var activeState = DomainAccountState.Active.ToString();
        var searchPattern = request.ToSqlLikePattern();
        var superAdminNormalizedName = DomainSystemRoleNames.SuperAdmin.ToUpperInvariant();

        IQueryable<AdminUserListQueryRow> userQuery =
            from user in db.AdminIdentityUsers.AsNoTracking()
            join profile in db.AdminUserProfiles.AsNoTracking() on user.Id equals profile.UserId into profileJoin
            from profile in profileJoin.DefaultIfEmpty()
            join accountState in db.AdminAccountStates.AsNoTracking() on user.Id equals accountState.UserId into stateJoin
            from accountState in stateJoin.DefaultIfEmpty()
            select new AdminUserListQueryRow
            {
                Id = user.Id,
                UserName = user.UserName ?? string.Empty,
                NormalizedUserName = user.NormalizedUserName,
                Email = user.Email ?? string.Empty,
                NormalizedEmail = user.NormalizedEmail,
                DisplayName = profile != null && profile.DisplayName != null && profile.DisplayName != string.Empty
                    ? profile.DisplayName
                    : (user.UserName ?? string.Empty),
                State = accountState != null ? accountState.State : null,
                CreatedAtUtc = accountState != null ? accountState.CreatedAtUtc : DateTimeOffset.MinValue,
                LastLoginAtUtc = accountState != null ? accountState.LastLoginAtUtc : null
            };

        if (!string.IsNullOrWhiteSpace(searchPattern))
        {
            userQuery = userQuery.Where(item =>
                EF.Functions.ILike(item.UserName, searchPattern) ||
                EF.Functions.ILike(item.Email, searchPattern) ||
                EF.Functions.ILike(item.DisplayName, searchPattern) ||
                db.AdminIdentityUserRoles
                    .Join(db.AdminIdentityRoles, userRole => userRole.RoleId, role => role.Id, (userRole, role) => new { userRole.UserId, role.NormalizedName })
                    .Any(match => match.UserId == item.Id && match.NormalizedName != null && EF.Functions.ILike(match.NormalizedName, searchPattern)));
        }

        userQuery = filter switch
        {
            AdminUserCatalogFilter.Active => userQuery.Where(item => item.State == activeState),
            AdminUserCatalogFilter.Inactive => userQuery.Where(item => item.State != activeState),
            AdminUserCatalogFilter.SuperAdmin => userQuery.Where(item =>
                db.AdminIdentityUserRoles
                    .Join(db.AdminIdentityRoles, userRole => userRole.RoleId, role => role.Id, (userRole, role) => new { userRole.UserId, role.NormalizedName })
                    .Any(match => match.UserId == item.Id && match.NormalizedName == superAdminNormalizedName)),
            _ => userQuery
        };

        var orderedQuery = ApplyAdminUserSort(
            userQuery,
            RbacCatalogProfiles.AdminUsers.ParseSort(request.Sort));

        var (pageRows, totalCount) = await orderedQuery.ToPageAsync(request.Page, request.PageSize, ct);

        if (pageRows.Count == 0)
            return new PaginatedResult<AdminUserListItemDto>(Array.Empty<AdminUserListItemDto>(), totalCount, request.Page, request.PageSize);

        var pageUserIds = pageRows.Select(item => item.Id).ToList();
        var roleAssignments = await db.AdminIdentityUserRoles.AsNoTracking()
            .Where(userRole => pageUserIds.Contains(userRole.UserId))
            .Join(db.AdminIdentityRoles.AsNoTracking(), userRole => userRole.RoleId, role => role.Id, (userRole, role) => new
            {
                userRole.UserId,
                RoleName = role.Name ?? string.Empty
            })
            .ToListAsync(ct);

        var rolesByUserId = roleAssignments
            .GroupBy(item => item.UserId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(item => item.RoleName).OrderBy(name => name).ToList().AsReadOnly());

        var pagedItems = pageRows
            .Select(item => new AdminUserListItemDto(
                item.Id,
                item.UserName,
                item.Email,
                item.DisplayName,
                item.State == activeState,
                rolesByUserId.TryGetValue(item.Id, out var roles) ? roles : Array.Empty<string>(),
                item.CreatedAtUtc,
                item.LastLoginAtUtc))
            .ToList()
            .AsReadOnly();

        return new PaginatedResult<AdminUserListItemDto>(pagedItems, totalCount, request.Page, request.PageSize);
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

    public async Task<PaginatedResult<RoleListItemDto>> ListRolesAsync(GridQuery request, RoleCatalogFilter filter, CancellationToken ct)
    {
        request = request.Normalize(maxPageSize: 100);
        var searchPattern = request.ToSqlLikePattern();

        var roleUserCounts =
            from userRole in db.AdminIdentityUserRoles.AsNoTracking()
            group userRole by userRole.RoleId
            into roleGroup
            select new
            {
                RoleId = roleGroup.Key,
                UserCount = roleGroup.Count()
            };

        IQueryable<RoleListQueryRow> roleQuery =
            from role in db.AdminIdentityRoles.AsNoTracking()
            join metadata in db.RoleMetadata.AsNoTracking() on role.Id equals metadata.RoleId into metadataJoin
            from metadata in metadataJoin.DefaultIfEmpty()
            join roleUserCount in roleUserCounts on role.Id equals roleUserCount.RoleId into roleUserCountJoin
            from roleUserCount in roleUserCountJoin.DefaultIfEmpty()
            select new RoleListQueryRow
            {
                Id = role.Id,
                Name = role.Name ?? string.Empty,
                Description = metadata != null ? metadata.Description : string.Empty,
                IsSystemRole = metadata != null ? metadata.IsSystemRole : null,
                Priority = metadata != null ? metadata.Priority : null,
                UserCount = roleUserCount != null ? roleUserCount.UserCount : null,
                CreatedAtUtc = metadata != null ? metadata.CreatedAtUtc : null
            };

        if (!string.IsNullOrWhiteSpace(searchPattern))
        {
            roleQuery = roleQuery.Where(item =>
                EF.Functions.ILike(item.Name, searchPattern) ||
                EF.Functions.ILike(item.Description, searchPattern));
        }

        roleQuery = filter switch
        {
            RoleCatalogFilter.System => roleQuery.Where(item => item.IsSystemRole == true),
            RoleCatalogFilter.Custom => roleQuery.Where(item => item.IsSystemRole != true),
            _ => roleQuery
        };

        var orderedQuery = ApplyRoleSort(
            roleQuery,
            RbacCatalogProfiles.Roles.ParseSort(request.Sort));

        var (pageRows, totalCount) = await orderedQuery.ToPageAsync(request.Page, request.PageSize, ct);

        var pagedItems = pageRows
            .Select(item => new RoleListItemDto(
                item.Id,
                item.Name,
                item.Description,
                item.IsSystemRole == true,
                item.Priority ?? 0,
                item.UserCount ?? 0,
                item.CreatedAtUtc ?? DateTimeOffset.MinValue))
            .ToList()
            .AsReadOnly();

        return new PaginatedResult<RoleListItemDto>(pagedItems, totalCount, request.Page, request.PageSize);
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
        await InvalidatePermissionSnapshotAsync(ct);

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
        await InvalidatePermissionSnapshotAsync(ct);
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

        await InvalidatePermissionSnapshotAsync(ct);
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
        await InvalidatePermissionSnapshotAsync(ct);
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
        await InvalidatePermissionSnapshotAsync(ct);
    }

    // ─── Permission Group Management ────────────────────────────────────────

    public async Task<PaginatedResult<PermissionGroupListItemDto>> ListPermissionGroupsAsync(GridQuery request, PermissionGroupCatalogFilter filter, CancellationToken ct)
    {
        request = request.Normalize(maxPageSize: 100);
        var searchPattern = request.ToSqlLikePattern();

        IQueryable<PermissionGroupListQueryRow> permissionGroupQuery =
            from permissionGroup in db.PermissionGroups.AsNoTracking()
            join permissionItem in db.PermissionGroupItems.AsNoTracking() on permissionGroup.Id equals permissionItem.PermissionGroupId into permissionJoin
            select new PermissionGroupListQueryRow
            {
                Id = permissionGroup.Id,
                Name = permissionGroup.Name,
                Description = permissionGroup.Description,
                IsSystemGroup = permissionGroup.IsSystemGroup,
                PermissionCount = permissionJoin.Count(),
                CreatedAtUtc = permissionGroup.CreatedAtUtc
            };

        if (!string.IsNullOrWhiteSpace(searchPattern))
        {
            permissionGroupQuery = permissionGroupQuery.Where(item =>
                EF.Functions.ILike(item.Name, searchPattern) ||
                EF.Functions.ILike(item.Description, searchPattern));
        }

        permissionGroupQuery = filter switch
        {
            PermissionGroupCatalogFilter.System => permissionGroupQuery.Where(item => item.IsSystemGroup),
            PermissionGroupCatalogFilter.Custom => permissionGroupQuery.Where(item => !item.IsSystemGroup),
            _ => permissionGroupQuery
        };

        var orderedQuery = ApplyPermissionGroupSort(
            permissionGroupQuery,
            RbacCatalogProfiles.PermissionGroups.ParseSort(request.Sort));

        var (pageRows, totalCount) = await orderedQuery.ToPageAsync(request.Page, request.PageSize, ct);

        var pagedItems = pageRows
            .Select(item => new PermissionGroupListItemDto(
                item.Id,
                item.Name,
                item.Description,
                item.IsSystemGroup,
                item.PermissionCount,
                item.CreatedAtUtc))
            .ToList()
            .AsReadOnly();

        return new PaginatedResult<PermissionGroupListItemDto>(pagedItems, totalCount, request.Page, request.PageSize);
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
        await InvalidatePermissionSnapshotAsync(ct);

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
        await InvalidatePermissionSnapshotAsync(ct);
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
        await InvalidatePermissionSnapshotAsync(ct);
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
        await InvalidatePermissionSnapshotAsync(ct);
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

    private static IOrderedQueryable<AdminUserListQueryRow> ApplyAdminUserSort(
        IQueryable<AdminUserListQueryRow> query,
        IReadOnlyList<GridSortTerm> sortTerms)
    {
        IOrderedQueryable<AdminUserListQueryRow>? ordered = null;

        foreach (var term in sortTerms)
        {
            ordered = term.Field switch
            {
                "email" => ApplyOrder(ordered, query, item => item.Email, term.Direction),
                "created" => ApplyOrder(ordered, query, item => item.CreatedAtUtc, term.Direction),
                "lastlogin" => ApplyOrder(ordered, query, item => item.LastLoginAtUtc ?? DateTimeOffset.MinValue, term.Direction),
                _ => ApplyOrder(ordered, query, item => item.DisplayName, term.Direction, item => item.UserName)
            };
            query = ordered;
        }

        return ordered ?? query.OrderBy(item => item.DisplayName).ThenBy(item => item.UserName);
    }

    private static IOrderedQueryable<RoleListQueryRow> ApplyRoleSort(
        IQueryable<RoleListQueryRow> query,
        IReadOnlyList<GridSortTerm> sortTerms)
    {
        IOrderedQueryable<RoleListQueryRow>? ordered = null;

        foreach (var term in sortTerms)
        {
            ordered = term.Field switch
            {
                "name" => ApplyOrder(ordered, query, item => item.Name, term.Direction, item => item.Priority),
                "users" => ApplyOrder(ordered, query, item => item.UserCount ?? 0, term.Direction, item => item.Name),
                "created" => ApplyOrder(ordered, query, item => item.CreatedAtUtc ?? DateTimeOffset.MinValue, term.Direction, item => item.Name),
                _ => ApplyOrder(ordered, query, item => item.Priority ?? 0, term.Direction, item => item.Name)
            };
            query = ordered;
        }

        return ordered ?? query.OrderByDescending(item => item.Priority ?? 0).ThenBy(item => item.Name);
    }

    private static IOrderedQueryable<PermissionGroupListQueryRow> ApplyPermissionGroupSort(
        IQueryable<PermissionGroupListQueryRow> query,
        IReadOnlyList<GridSortTerm> sortTerms)
    {
        IOrderedQueryable<PermissionGroupListQueryRow>? ordered = null;

        foreach (var term in sortTerms)
        {
            ordered = term.Field switch
            {
                "permissions" => ApplyOrder(ordered, query, item => item.PermissionCount, term.Direction, item => item.Name),
                "created" => ApplyOrder(ordered, query, item => item.CreatedAtUtc, term.Direction, item => item.Name),
                _ => ApplyOrder(ordered, query, item => item.Name, term.Direction, item => item.PermissionCount)
            };
            query = ordered;
        }

        return ordered ?? query.OrderBy(item => item.Name).ThenByDescending(item => item.PermissionCount);
    }

    private static IOrderedQueryable<T> ApplyOrder<T, TPrimary>(
        IOrderedQueryable<T>? ordered,
        IQueryable<T> source,
        System.Linq.Expressions.Expression<Func<T, TPrimary>> keySelector,
        GridSortDirection direction)
        => ApplyOrder<T, TPrimary, object>(ordered, source, keySelector, direction, fallbackSelector: null);

    private static IOrderedQueryable<T> ApplyOrder<T, TPrimary, TFallback>(
        IOrderedQueryable<T>? ordered,
        IQueryable<T> source,
        System.Linq.Expressions.Expression<Func<T, TPrimary>> keySelector,
        GridSortDirection direction,
        System.Linq.Expressions.Expression<Func<T, TFallback>>? fallbackSelector)
    {
        if (ordered is null)
        {
            var first = direction == GridSortDirection.Desc
                ? source.OrderByDescending(keySelector)
                : source.OrderBy(keySelector);

            if (fallbackSelector is null)
                return first;

            return direction == GridSortDirection.Desc
                ? first.ThenByDescending(fallbackSelector)
                : first.ThenBy(fallbackSelector);
        }

        var next = direction == GridSortDirection.Desc
            ? ordered.ThenByDescending(keySelector)
            : ordered.ThenBy(keySelector);

        if (fallbackSelector is null)
            return next;

        return direction == GridSortDirection.Desc
            ? next.ThenByDescending(fallbackSelector)
            : next.ThenBy(fallbackSelector);
    }

    private async Task<RbacStateEntity> EnsurePermissionSnapshotStateAsync(CancellationToken ct)
    {
        var state = await db.RbacStates.FirstOrDefaultAsync(entry => entry.Key == PermissionSnapshotStateKey, ct);
        if (state is not null)
        {
            return state;
        }

        state = new RbacStateEntity
        {
            Key = PermissionSnapshotStateKey,
            Version = 1,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        db.RbacStates.Add(state);
        await db.SaveChangesAsync(ct);
        return state;
    }

    private sealed class AdminUserListQueryRow
    {
        public required string Id { get; init; }
        public required string UserName { get; init; }
        public string? NormalizedUserName { get; init; }
        public required string Email { get; init; }
        public string? NormalizedEmail { get; init; }
        public required string DisplayName { get; init; }
        public string? State { get; init; }
        public DateTimeOffset CreatedAtUtc { get; init; }
        public DateTimeOffset? LastLoginAtUtc { get; init; }
    }

    private sealed class RoleListQueryRow
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Description { get; init; }
        public bool? IsSystemRole { get; init; }
        public int? Priority { get; init; }
        public int? UserCount { get; init; }
        public DateTimeOffset? CreatedAtUtc { get; init; }
    }

    private sealed class PermissionGroupListQueryRow
    {
        public Guid Id { get; init; }
        public required string Name { get; init; }
        public required string Description { get; init; }
        public bool IsSystemGroup { get; init; }
        public int PermissionCount { get; init; }
        public DateTimeOffset CreatedAtUtc { get; init; }
    }
}
