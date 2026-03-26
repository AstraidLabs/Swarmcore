using BeeTracker.Contracts.Identity;

namespace Tracker.AdminService.Application;

public static class AdminClaimTypes
{
    public const string Permission = "permission";
    public const string AuthenticatedAt = "admin_authenticated_at";
    public const string PermissionSnapshotVersion = "admin_permission_snapshot_version";
}

public static class AdminPermissions
{
    public const string DashboardView = AdminPermissionCatalog.DashboardView;
    public const string ProfileView = AdminPermissionCatalog.ProfileView;
    public const string ProfileEdit = AdminPermissionCatalog.ProfileEdit;
    public const string UsersView = AdminPermissionCatalog.UsersView;
    public const string UsersCreate = AdminPermissionCatalog.UsersCreate;
    public const string UsersEdit = AdminPermissionCatalog.UsersEdit;
    public const string UsersActivate = AdminPermissionCatalog.UsersActivate;
    public const string UsersDeactivate = AdminPermissionCatalog.UsersDeactivate;
    public const string UsersAssignRoles = AdminPermissionCatalog.UsersAssignRoles;
    public const string UsersResetPassword = AdminPermissionCatalog.UsersResetPassword;
    public const string RolesView = AdminPermissionCatalog.RolesView;
    public const string RolesCreate = AdminPermissionCatalog.RolesCreate;
    public const string RolesEdit = AdminPermissionCatalog.RolesEdit;
    public const string RolesDelete = AdminPermissionCatalog.RolesDelete;
    public const string RolesAssignPermissions = AdminPermissionCatalog.RolesAssignPermissions;
    public const string PermissionGroupsView = AdminPermissionCatalog.PermissionGroupsView;
    public const string PermissionGroupsCreate = AdminPermissionCatalog.PermissionGroupsCreate;
    public const string PermissionGroupsEdit = AdminPermissionCatalog.PermissionGroupsEdit;
    public const string PermissionGroupsDelete = AdminPermissionCatalog.PermissionGroupsDelete;
    public const string PermissionCatalogView = AdminPermissionCatalog.PermissionCatalogView;
    public const string AuditView = AdminPermissionCatalog.AuditView;
    public const string TorrentsView = AdminPermissionCatalog.TorrentsView;
    public const string TorrentsEdit = AdminPermissionCatalog.TorrentsEdit;
    public const string TrackerPoliciesView = AdminPermissionCatalog.TrackerPoliciesView;
    public const string TrackerPoliciesEdit = AdminPermissionCatalog.TrackerPoliciesEdit;
    public const string PasskeysView = AdminPermissionCatalog.PasskeysView;
    public const string PasskeysManage = AdminPermissionCatalog.PasskeysManage;
    public const string TrackerAccessView = AdminPermissionCatalog.TrackerAccessView;
    public const string TrackerAccessManage = AdminPermissionCatalog.TrackerAccessManage;
    public const string BansView = AdminPermissionCatalog.BansView;
    public const string BansManage = AdminPermissionCatalog.BansManage;
    public const string NodesView = AdminPermissionCatalog.NodesView;
    public const string StatsView = AdminPermissionCatalog.StatsView;
    public const string SystemSettingsView = AdminPermissionCatalog.SystemSettingsView;
    public const string SystemSettingsEdit = AdminPermissionCatalog.SystemSettingsEdit;
    public const string MaintenanceExecute = AdminPermissionCatalog.MaintenanceExecute;
}

public static class AdminAuthorizationPolicies
{
    public static string ForPermission(string permissionKey) => $"admin.permission:{permissionKey}";
}
