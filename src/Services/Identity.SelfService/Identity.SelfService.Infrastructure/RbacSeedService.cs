using Identity.SelfService.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Identity.SelfService.Infrastructure;

public sealed class RbacSeedService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<RbacSeedService> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfServiceDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        await SeedPermissionDefinitionsAsync(db, cancellationToken);
        await SeedSystemRolesAsync(db, roleManager, cancellationToken);
        await SeedSystemPermissionGroupsAsync(db, cancellationToken);
        await SeedDefaultRolePermissionsAsync(db, roleManager, cancellationToken);

        logger.LogInformation("RBAC seed completed.");
    }

    private async Task SeedPermissionDefinitionsAsync(SelfServiceDbContext db, CancellationToken ct)
    {
        foreach (var (key, name, description, category) in PermissionCatalog.All)
        {
            ct.ThrowIfCancellationRequested();

            var existing = await db.PermissionDefinitions.FirstOrDefaultAsync(p => p.Key == key, ct);
            if (existing is not null)
            {
                existing.Name = name;
                existing.Description = description;
                existing.Category = category;
                existing.IsSystemPermission = true;
                continue;
            }

            db.PermissionDefinitions.Add(new PermissionDefinitionEntity
            {
                Id = Guid.NewGuid(),
                Key = key,
                Name = name,
                Description = description,
                Category = category,
                IsSystemPermission = true
            });
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} permission definitions.", PermissionCatalog.All.Count);
    }

    private async Task SeedSystemRolesAsync(SelfServiceDbContext db, RoleManager<IdentityRole> roleManager, CancellationToken ct)
    {
        var systemRoles = new[]
        {
            (Name: SystemRoleNames.SuperAdmin, Description: "Full system access. Cannot be deleted.", Priority: 1000),
            (Name: SystemRoleNames.Admin, Description: "Administrative access with most permissions.", Priority: 900),
            (Name: SystemRoleNames.Moderator, Description: "Content and user moderation.", Priority: 500),
            (Name: SystemRoleNames.Support, Description: "Read-only support access.", Priority: 100),
        };

        foreach (var (name, description, priority) in systemRoles)
        {
            ct.ThrowIfCancellationRequested();

            if (!await roleManager.RoleExistsAsync(name))
            {
                var result = await roleManager.CreateAsync(new IdentityRole(name));
                if (!result.Succeeded)
                {
                    logger.LogError("Failed to create system role '{RoleName}': {Errors}", name,
                        string.Join(", ", result.Errors.Select(e => e.Description)));
                    continue;
                }
            }

            var role = await roleManager.FindByNameAsync(name);
            if (role is null) continue;

            var metadata = await db.RoleMetadata.FirstOrDefaultAsync(m => m.RoleId == role.Id, ct);
            var now = DateTimeOffset.UtcNow;

            if (metadata is null)
            {
                db.RoleMetadata.Add(new RoleMetadataEntity
                {
                    RoleId = role.Id,
                    Description = description,
                    IsSystemRole = true,
                    Priority = priority,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
            }
            else
            {
                metadata.Description = description;
                metadata.IsSystemRole = true;
                metadata.Priority = priority;
                metadata.UpdatedAtUtc = now;
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} system roles.", systemRoles.Length);
    }

    private async Task SeedSystemPermissionGroupsAsync(SelfServiceDbContext db, CancellationToken ct)
    {
        var groups = new[]
        {
            (Name: SystemPermissionGroupNames.FullAccess, Description: "All permissions.", Permissions: PermissionCatalog.All.Select(p => p.Key).ToArray()),
            (Name: SystemPermissionGroupNames.UserManagement, Description: "User management permissions.", Permissions: new[]
            {
                PermissionCatalog.UsersView, PermissionCatalog.UsersCreate, PermissionCatalog.UsersEdit,
                PermissionCatalog.UsersActivate, PermissionCatalog.UsersDeactivate,
                PermissionCatalog.UsersAssignRoles, PermissionCatalog.UsersResetPassword,
                PermissionCatalog.RolesView, PermissionCatalog.PermissionGroupsView,
            }),
            (Name: SystemPermissionGroupNames.TrackerManagement, Description: "Tracker management permissions.", Permissions: new[]
            {
                PermissionCatalog.TorrentsView, PermissionCatalog.TorrentsEdit,
                PermissionCatalog.TrackerPoliciesView, PermissionCatalog.TrackerPoliciesEdit,
                PermissionCatalog.BansView, PermissionCatalog.BansManage,
                PermissionCatalog.PasskeysView, PermissionCatalog.PasskeysRegenerate,
                PermissionCatalog.NodesView, PermissionCatalog.StatsView,
            }),
            (Name: SystemPermissionGroupNames.ReadOnly, Description: "Read-only access.", Permissions: new[]
            {
                PermissionCatalog.DashboardView, PermissionCatalog.ProfileView,
                PermissionCatalog.UsersView, PermissionCatalog.RolesView,
                PermissionCatalog.PermissionGroupsView, PermissionCatalog.AuditView,
                PermissionCatalog.TorrentsView, PermissionCatalog.TrackerPoliciesView,
                PermissionCatalog.BansView, PermissionCatalog.PasskeysView,
                PermissionCatalog.NodesView, PermissionCatalog.StatsView,
                PermissionCatalog.SystemSettingsView,
            }),
        };

        var allPermDefs = await db.PermissionDefinitions.ToListAsync(ct);
        var permLookup = allPermDefs.ToDictionary(p => p.Key, p => p.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, description, permissions) in groups)
        {
            ct.ThrowIfCancellationRequested();

            var existing = await db.PermissionGroups.FirstOrDefaultAsync(g => g.Name == name, ct);
            var now = DateTimeOffset.UtcNow;

            if (existing is null)
            {
                existing = new PermissionGroupEntity
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    Description = description,
                    IsSystemGroup = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };
                db.PermissionGroups.Add(existing);
                await db.SaveChangesAsync(ct);
            }
            else
            {
                existing.Description = description;
                existing.IsSystemGroup = true;
                existing.UpdatedAtUtc = now;
            }

            // Replace group items
            var existingItems = await db.PermissionGroupItems.Where(pgi => pgi.PermissionGroupId == existing.Id).ToListAsync(ct);
            db.PermissionGroupItems.RemoveRange(existingItems);

            foreach (var permKey in permissions)
            {
                if (permLookup.TryGetValue(permKey, out var permId))
                {
                    db.PermissionGroupItems.Add(new PermissionGroupItemEntity
                    {
                        PermissionGroupId = existing.Id,
                        PermissionDefinitionId = permId
                    });
                }
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} system permission groups.", groups.Length);
    }

    private async Task SeedDefaultRolePermissionsAsync(SelfServiceDbContext db, RoleManager<IdentityRole> roleManager, CancellationToken ct)
    {
        var allPermDefs = await db.PermissionDefinitions.ToListAsync(ct);
        var permLookup = allPermDefs.ToDictionary(p => p.Key, p => p.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var (roleName, permissionKeys) in PermissionCatalog.DefaultRolePermissions)
        {
            ct.ThrowIfCancellationRequested();

            // SuperAdmin doesn't need explicit permissions - it gets all implicitly
            if (string.Equals(roleName, SystemRoleNames.SuperAdmin, StringComparison.OrdinalIgnoreCase))
                continue;

            var role = await roleManager.FindByNameAsync(roleName);
            if (role is null) continue;

            // Only seed if role has no direct permissions yet
            var existingPerms = await db.RolePermissions.AnyAsync(rp => rp.RoleId == role.Id, ct);
            if (existingPerms) continue;

            foreach (var key in permissionKeys)
            {
                if (permLookup.TryGetValue(key, out var permId))
                {
                    db.RolePermissions.Add(new RolePermissionEntity
                    {
                        RoleId = role.Id,
                        PermissionDefinitionId = permId
                    });
                }
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded default role permissions.");
    }
}
