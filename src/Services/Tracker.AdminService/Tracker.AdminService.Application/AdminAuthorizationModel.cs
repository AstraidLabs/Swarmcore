namespace Tracker.AdminService.Application;

public static class AdminClaimTypes
{
    public const string Permission = "permission";
    public const string AuthenticatedAt = "admin_authenticated_at";
}

public static class AdminPermissions
{
    public const string Read = "admin.read";
    public const string TorrentWrite = "torrents.write";
    public const string PasskeyWrite = "passkeys.write";
    public const string PermissionWrite = "permissions.write";
    public const string BanWrite = "bans.write";
    public const string MaintenanceExecute = "maintenance.execute";
}

public static class AdminAuthorizationPolicies
{
    public const string Read = "admin.read.policy";
    public const string TorrentWrite = "admin.torrents.write.policy";
    public const string PasskeyWrite = "admin.passkeys.write.policy";
    public const string PermissionWrite = "admin.permissions.write.policy";
    public const string BanWrite = "admin.bans.write.policy";
    public const string MaintenanceExecute = "admin.maintenance.execute.policy";
}
