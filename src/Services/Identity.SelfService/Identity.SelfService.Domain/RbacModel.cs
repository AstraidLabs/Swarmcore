using BeeTracker.Contracts.Identity;

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
    public const string SuperAdmin = AdminSystemRoleNames.SuperAdmin;
    public const string Admin = AdminSystemRoleNames.Admin;
    public const string Moderator = AdminSystemRoleNames.Moderator;
    public const string Support = AdminSystemRoleNames.Support;

    public static readonly IReadOnlyList<string> All = AdminSystemRoleNames.All;
}

// ─── Permission Catalog ─────────────────────────────────────────────────────

public static class PermissionCatalog
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
    public const string BansView = AdminPermissionCatalog.BansView;
    public const string BansManage = AdminPermissionCatalog.BansManage;
    public const string PasskeysView = AdminPermissionCatalog.PasskeysView;
    public const string PasskeysRegenerate = AdminPermissionCatalog.PasskeysManage;
    public const string TrackerAccessView = AdminPermissionCatalog.TrackerAccessView;
    public const string TrackerAccessManage = AdminPermissionCatalog.TrackerAccessManage;
    public const string NodesView = AdminPermissionCatalog.NodesView;
    public const string StatsView = AdminPermissionCatalog.StatsView;
    public const string SystemSettingsView = AdminPermissionCatalog.SystemSettingsView;
    public const string SystemSettingsEdit = AdminPermissionCatalog.SystemSettingsEdit;
    public const string MaintenanceExecute = AdminPermissionCatalog.MaintenanceExecute;

    public static IReadOnlyList<(string Key, string Name, string Description, string Category)> All => AdminPermissionCatalog.All;

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultRolePermissions => AdminPermissionCatalog.DefaultRolePermissions;
}

// ─── System Permission Group Names ──────────────────────────────────────────

public static class SystemPermissionGroupNames
{
    public const string FullAccess = AdminSystemPermissionGroupNames.FullAccess;
    public const string UserManagement = AdminSystemPermissionGroupNames.UserManagement;
    public const string TrackerManagement = AdminSystemPermissionGroupNames.TrackerManagement;
    public const string ReadOnly = AdminSystemPermissionGroupNames.ReadOnly;
}
