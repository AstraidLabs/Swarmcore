namespace Identity.SelfService.Domain;

// ─── Admin User Profile Extension ───────────────────────────────────────────

public sealed class AdminUserProfile
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string TimeZone { get; set; } = "UTC";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

// ─── Permission Definition ──────────────────────────────────────────────────

public sealed class PermissionDefinition
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsSystemPermission { get; set; }
}

// ─── Permission Group ───────────────────────────────────────────────────────

public sealed class PermissionGroup
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsSystemGroup { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

// ─── Permission Group Item ──────────────────────────────────────────────────

public sealed class PermissionGroupItem
{
    public Guid PermissionGroupId { get; set; }
    public Guid PermissionDefinitionId { get; set; }
}

// ─── Role Permission Group ──────────────────────────────────────────────────

public sealed class RolePermissionGroup
{
    public string RoleId { get; set; } = string.Empty;
    public Guid PermissionGroupId { get; set; }
}

// ─── Role Permission (direct) ───────────────────────────────────────────────

public sealed class RolePermission
{
    public string RoleId { get; set; } = string.Empty;
    public Guid PermissionDefinitionId { get; set; }
}

// ─── Role Metadata Extension ────────────────────────────────────────────────

public sealed class RoleMetadata
{
    public string RoleId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsSystemRole { get; set; }
    public int Priority { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

// ─── Admin Audit Log ────────────────────────────────────────────────────────

public sealed class AdminAuditLog
{
    public Guid Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string? TargetEntityType { get; set; }
    public string? TargetEntityId { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}

// ─── System Role Names ──────────────────────────────────────────────────────

public static class SystemRoleNames
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";
    public const string Moderator = "Moderator";
    public const string Support = "Support";

    public static readonly IReadOnlyList<string> All = [SuperAdmin, Admin, Moderator, Support];
}

// ─── Permission Catalog ─────────────────────────────────────────────────────

public static class PermissionCatalog
{
    // Dashboard
    public const string DashboardView = "admin.dashboard.view";

    // Profile
    public const string ProfileView = "admin.profile.view";
    public const string ProfileEdit = "admin.profile.edit";

    // Users
    public const string UsersView = "admin.users.view";
    public const string UsersCreate = "admin.users.create";
    public const string UsersEdit = "admin.users.edit";
    public const string UsersActivate = "admin.users.activate";
    public const string UsersDeactivate = "admin.users.deactivate";
    public const string UsersAssignRoles = "admin.users.assign_roles";
    public const string UsersResetPassword = "admin.users.reset_password";

    // Roles
    public const string RolesView = "admin.roles.view";
    public const string RolesCreate = "admin.roles.create";
    public const string RolesEdit = "admin.roles.edit";
    public const string RolesDelete = "admin.roles.delete";
    public const string RolesAssignPermissions = "admin.roles.assign_permissions";

    // Permission Groups
    public const string PermissionGroupsView = "admin.permission_groups.view";
    public const string PermissionGroupsCreate = "admin.permission_groups.create";
    public const string PermissionGroupsEdit = "admin.permission_groups.edit";
    public const string PermissionGroupsDelete = "admin.permission_groups.delete";

    // Audit
    public const string AuditView = "admin.audit.view";

    // Tracker configuration
    public const string TorrentsView = "admin.torrents.view";
    public const string TorrentsEdit = "admin.torrents.edit";
    public const string TrackerPoliciesView = "admin.tracker_policies.view";
    public const string TrackerPoliciesEdit = "admin.tracker_policies.edit";
    public const string BansView = "admin.bans.view";
    public const string BansManage = "admin.bans.manage";
    public const string PasskeysView = "admin.passkeys.view";
    public const string PasskeysRegenerate = "admin.passkeys.regenerate";
    public const string NodesView = "admin.nodes.view";
    public const string StatsView = "admin.stats.view";
    public const string SystemSettingsView = "admin.system_settings.view";
    public const string SystemSettingsEdit = "admin.system_settings.edit";

    public static IReadOnlyList<(string Key, string Name, string Description, string Category)> All =>
    [
        (DashboardView, "View Dashboard", "Access the admin dashboard", "Dashboard"),
        (ProfileView, "View Profile", "View own admin profile", "Profile"),
        (ProfileEdit, "Edit Profile", "Edit own admin profile", "Profile"),
        (UsersView, "View Users", "List and view admin user details", "Users"),
        (UsersCreate, "Create Users", "Create new admin users", "Users"),
        (UsersEdit, "Edit Users", "Edit admin user details", "Users"),
        (UsersActivate, "Activate Users", "Activate admin user accounts", "Users"),
        (UsersDeactivate, "Deactivate Users", "Deactivate admin user accounts", "Users"),
        (UsersAssignRoles, "Assign Roles", "Assign roles to admin users", "Users"),
        (UsersResetPassword, "Reset Password", "Reset admin user passwords", "Users"),
        (RolesView, "View Roles", "List and view role definitions", "Roles"),
        (RolesCreate, "Create Roles", "Create new roles", "Roles"),
        (RolesEdit, "Edit Roles", "Edit existing roles", "Roles"),
        (RolesDelete, "Delete Roles", "Delete non-system roles", "Roles"),
        (RolesAssignPermissions, "Assign Permissions to Roles", "Manage role permission assignments", "Roles"),
        (PermissionGroupsView, "View Permission Groups", "List and view permission groups", "Permission Groups"),
        (PermissionGroupsCreate, "Create Permission Groups", "Create new permission groups", "Permission Groups"),
        (PermissionGroupsEdit, "Edit Permission Groups", "Edit existing permission groups", "Permission Groups"),
        (PermissionGroupsDelete, "Delete Permission Groups", "Delete non-system permission groups", "Permission Groups"),
        (AuditView, "View Audit Log", "View the admin audit log", "Audit"),
        (TorrentsView, "View Torrents", "View torrent configuration", "Tracker"),
        (TorrentsEdit, "Edit Torrents", "Modify torrent configuration", "Tracker"),
        (TrackerPoliciesView, "View Tracker Policies", "View tracker policy settings", "Tracker"),
        (TrackerPoliciesEdit, "Edit Tracker Policies", "Modify tracker policy settings", "Tracker"),
        (BansView, "View Bans", "View ban rules", "Tracker"),
        (BansManage, "Manage Bans", "Create, edit, and remove ban rules", "Tracker"),
        (PasskeysView, "View Passkeys", "View passkey information", "Tracker"),
        (PasskeysRegenerate, "Regenerate Passkeys", "Regenerate user passkeys", "Tracker"),
        (NodesView, "View Nodes", "View cluster node information", "Monitoring"),
        (StatsView, "View Stats", "View tracker statistics", "Monitoring"),
        (SystemSettingsView, "View System Settings", "View system configuration", "System"),
        (SystemSettingsEdit, "Edit System Settings", "Modify system configuration", "System"),
    ];

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultRolePermissions => new Dictionary<string, IReadOnlyList<string>>
    {
        [SystemRoleNames.SuperAdmin] = All.Select(p => p.Key).ToList(),
        [SystemRoleNames.Admin] = [
            DashboardView, ProfileView, ProfileEdit,
            UsersView, UsersCreate, UsersEdit, UsersActivate, UsersDeactivate, UsersAssignRoles, UsersResetPassword,
            RolesView, RolesCreate, RolesEdit, RolesAssignPermissions,
            PermissionGroupsView, PermissionGroupsCreate, PermissionGroupsEdit,
            AuditView,
            TorrentsView, TorrentsEdit, TrackerPoliciesView, TrackerPoliciesEdit,
            BansView, BansManage, PasskeysView, PasskeysRegenerate,
            NodesView, StatsView, SystemSettingsView,
        ],
        [SystemRoleNames.Moderator] = [
            DashboardView, ProfileView, ProfileEdit,
            UsersView,
            RolesView,
            PermissionGroupsView,
            AuditView,
            TorrentsView, TorrentsEdit, TrackerPoliciesView,
            BansView, BansManage,
            PasskeysView,
            NodesView, StatsView,
        ],
        [SystemRoleNames.Support] = [
            DashboardView, ProfileView, ProfileEdit,
            UsersView,
            RolesView,
            PermissionGroupsView,
            AuditView,
            TorrentsView, TrackerPoliciesView,
            BansView,
            PasskeysView,
            NodesView, StatsView,
        ],
    };
}

// ─── System Permission Group Names ──────────────────────────────────────────

public static class SystemPermissionGroupNames
{
    public const string FullAccess = "Full Access";
    public const string UserManagement = "User Management";
    public const string TrackerManagement = "Tracker Management";
    public const string ReadOnly = "Read Only Access";
}
