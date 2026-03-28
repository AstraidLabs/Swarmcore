using BeeTracker.BuildingBlocks.Application.Queries;

namespace Identity.SelfService.Application;

public static class RbacCatalogProfiles
{
    public static readonly GridCatalogProfile AdminUsers = new(
        ["active", "inactive", "superadmin"],
        ["name", "email", "created", "lastlogin"],
        [new("name", GridSortDirection.Asc), new("created", GridSortDirection.Desc)]);

    public static readonly GridCatalogProfile Roles = new(
        ["system", "custom"],
        ["priority", "name", "users", "created"],
        [new("priority", GridSortDirection.Desc), new("name", GridSortDirection.Asc)]);

    public static readonly GridCatalogProfile PermissionGroups = new(
        ["system", "custom"],
        ["name", "permissions", "created"],
        [new("name", GridSortDirection.Asc), new("permissions", GridSortDirection.Desc)]);
}
