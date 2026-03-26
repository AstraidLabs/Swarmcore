namespace BeeTracker.Contracts.Identity;

public static class AdminSystemRoleNames
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";
    public const string Moderator = "Moderator";
    public const string Support = "Support";

    public static readonly IReadOnlyList<string> All = [SuperAdmin, Admin, Moderator, Support];
}

public static class AdminSystemPermissionGroupNames
{
    public const string FullAccess = "Full Access";
    public const string UserManagement = "User Management";
    public const string TrackerManagement = "Tracker Management";
    public const string ReadOnly = "Read Only Access";
}

public static class AdminPermissionCatalog
{
    public const string DashboardView = "admin.dashboard.view";

    public const string ProfileView = "admin.profile.view";
    public const string ProfileEdit = "admin.profile.edit";

    public const string UsersView = "admin.users.view";
    public const string UsersCreate = "admin.users.create";
    public const string UsersEdit = "admin.users.edit";
    public const string UsersActivate = "admin.users.activate";
    public const string UsersDeactivate = "admin.users.deactivate";
    public const string UsersAssignRoles = "admin.users.assign_roles";
    public const string UsersResetPassword = "admin.users.reset_password";

    public const string RolesView = "admin.roles.view";
    public const string RolesCreate = "admin.roles.create";
    public const string RolesEdit = "admin.roles.edit";
    public const string RolesDelete = "admin.roles.delete";
    public const string RolesAssignPermissions = "admin.roles.assign_permissions";

    public const string PermissionGroupsView = "admin.permission_groups.view";
    public const string PermissionGroupsCreate = "admin.permission_groups.create";
    public const string PermissionGroupsEdit = "admin.permission_groups.edit";
    public const string PermissionGroupsDelete = "admin.permission_groups.delete";

    public const string PermissionCatalogView = "admin.permission_catalog.view";

    public const string AuditView = "admin.audit.view";

    public const string TorrentsView = "admin.torrents.view";
    public const string TorrentsEdit = "admin.torrents.edit";
    public const string TrackerPoliciesView = "admin.tracker_policies.view";
    public const string TrackerPoliciesEdit = "admin.tracker_policies.edit";
    public const string PasskeysView = "admin.passkeys.view";
    public const string PasskeysManage = "admin.passkeys.manage";
    public const string TrackerAccessView = "admin.tracker_access.view";
    public const string TrackerAccessManage = "admin.tracker_access.manage";
    public const string BansView = "admin.bans.view";
    public const string BansManage = "admin.bans.manage";
    public const string NodesView = "admin.nodes.view";
    public const string StatsView = "admin.stats.view";
    public const string SystemSettingsView = "admin.system_settings.view";
    public const string SystemSettingsEdit = "admin.system_settings.edit";
    public const string MaintenanceExecute = "admin.maintenance.execute";

    public static IReadOnlyList<(string Key, string Name, string Description, string Category)> All =>
    [
        (DashboardView, "View Dashboard", "Access the admin dashboard.", "Dashboard"),
        (ProfileView, "View Profile", "View the current admin profile.", "Profile"),
        (ProfileEdit, "Edit Profile", "Update the current admin profile.", "Profile"),
        (UsersView, "View Users", "List and inspect admin users.", "Users"),
        (UsersCreate, "Create Users", "Create new admin users.", "Users"),
        (UsersEdit, "Edit Users", "Update admin user profile data.", "Users"),
        (UsersActivate, "Activate Users", "Activate admin user accounts.", "Users"),
        (UsersDeactivate, "Deactivate Users", "Deactivate admin user accounts.", "Users"),
        (UsersAssignRoles, "Assign Roles", "Assign roles to admin users.", "Users"),
        (UsersResetPassword, "Reset Passwords", "Reset admin user passwords.", "Users"),
        (RolesView, "View Roles", "List and inspect role definitions.", "Roles"),
        (RolesCreate, "Create Roles", "Create custom roles.", "Roles"),
        (RolesEdit, "Edit Roles", "Update role metadata.", "Roles"),
        (RolesDelete, "Delete Roles", "Delete non-system roles.", "Roles"),
        (RolesAssignPermissions, "Assign Role Permissions", "Manage role permissions and permission groups.", "Roles"),
        (PermissionGroupsView, "View Permission Groups", "List and inspect permission groups.", "Permission Groups"),
        (PermissionGroupsCreate, "Create Permission Groups", "Create custom permission groups.", "Permission Groups"),
        (PermissionGroupsEdit, "Edit Permission Groups", "Update permission groups.", "Permission Groups"),
        (PermissionGroupsDelete, "Delete Permission Groups", "Delete non-system permission groups.", "Permission Groups"),
        (PermissionCatalogView, "View Permission Catalog", "Inspect the full permission catalog.", "Permission Catalog"),
        (AuditView, "View Audit Log", "View audit history for privileged operations.", "Audit"),
        (TorrentsView, "View Torrents", "Inspect torrent configuration.", "Tracker"),
        (TorrentsEdit, "Edit Torrents", "Update torrent records.", "Tracker"),
        (TrackerPoliciesView, "View Tracker Policies", "Inspect tracker policies.", "Tracker"),
        (TrackerPoliciesEdit, "Edit Tracker Policies", "Update tracker policies.", "Tracker"),
        (PasskeysView, "View Passkeys", "Inspect passkeys.", "Tracker"),
        (PasskeysManage, "Manage Passkeys", "Create, revoke, and rotate passkeys.", "Tracker"),
        (TrackerAccessView, "View Tracker Access", "Inspect tracker access flags.", "Tracker"),
        (TrackerAccessManage, "Manage Tracker Access", "Update tracker access flags.", "Tracker"),
        (BansView, "View Bans", "Inspect ban rules.", "Tracker"),
        (BansManage, "Manage Bans", "Create, expire, and delete ban rules.", "Tracker"),
        (NodesView, "View Nodes", "Inspect cluster node state.", "Monitoring"),
        (StatsView, "View Stats", "Inspect tracker statistics and shard diagnostics.", "Monitoring"),
        (SystemSettingsView, "View System Settings", "Inspect system-level settings.", "System"),
        (SystemSettingsEdit, "Edit System Settings", "Update system-level settings.", "System"),
        (MaintenanceExecute, "Execute Maintenance", "Run privileged maintenance actions.", "System"),
    ];

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultRolePermissions => new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
    {
        [AdminSystemRoleNames.SuperAdmin] = All.Select(static permission => permission.Key).ToArray(),
        [AdminSystemRoleNames.Admin] =
        [
            DashboardView, ProfileView, ProfileEdit,
            UsersView, UsersCreate, UsersEdit, UsersActivate, UsersDeactivate, UsersAssignRoles, UsersResetPassword,
            RolesView, RolesCreate, RolesEdit, RolesAssignPermissions,
            PermissionGroupsView, PermissionGroupsCreate, PermissionGroupsEdit,
            PermissionCatalogView,
            AuditView,
            TorrentsView, TorrentsEdit, TrackerPoliciesView, TrackerPoliciesEdit,
            PasskeysView, PasskeysManage,
            TrackerAccessView, TrackerAccessManage,
            BansView, BansManage,
            NodesView, StatsView,
            SystemSettingsView, MaintenanceExecute
        ],
        [AdminSystemRoleNames.Moderator] =
        [
            DashboardView, ProfileView, ProfileEdit,
            UsersView,
            RolesView,
            PermissionGroupsView,
            PermissionCatalogView,
            AuditView,
            TorrentsView, TrackerPoliciesView, TrackerPoliciesEdit,
            PasskeysView,
            TrackerAccessView,
            BansView, BansManage,
            NodesView, StatsView
        ],
        [AdminSystemRoleNames.Support] =
        [
            DashboardView, ProfileView, ProfileEdit,
            UsersView,
            RolesView,
            PermissionGroupsView,
            PermissionCatalogView,
            AuditView,
            TorrentsView, TrackerPoliciesView,
            PasskeysView,
            TrackerAccessView,
            BansView,
            NodesView, StatsView,
            SystemSettingsView
        ]
    };

    public static IReadOnlySet<string> PrivilegedPermissions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        UsersCreate,
        UsersEdit,
        UsersActivate,
        UsersDeactivate,
        UsersAssignRoles,
        UsersResetPassword,
        RolesCreate,
        RolesEdit,
        RolesDelete,
        RolesAssignPermissions,
        PermissionGroupsCreate,
        PermissionGroupsEdit,
        PermissionGroupsDelete,
        TrackerPoliciesEdit,
        PasskeysManage,
        TrackerAccessManage,
        BansManage,
        SystemSettingsEdit,
        MaintenanceExecute
    };
}
