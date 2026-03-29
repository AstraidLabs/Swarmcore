using BeeTracker.BuildingBlocks.Application.Queries;

namespace Tracker.AdminService.Application;

public static class AdminCatalogProfiles
{
    public static readonly GridCatalogProfile Audit = new(
        ["success", "failure", "warn"],
        ["occurred", "action", "severity", "actor"],
        [new("occurred", GridSortDirection.Desc)]);

    public static readonly GridCatalogProfile Maintenance = new(
        ["completed", "failed", "running"],
        ["requested", "operation", "status"],
        [new("requested", GridSortDirection.Desc)]);

    public static readonly GridCatalogProfile Torrents = new(
        ["enabled", "disabled", "private", "public"],
        ["infohash", "enabled", "private", "interval"],
        [new("infohash", GridSortDirection.Asc)]);

    public static readonly GridCatalogProfile Passkeys = new(
        ["active", "revoked", "expired"],
        ["userid", "expires", "version"],
        [new("userid", GridSortDirection.Asc)]);

    public static readonly GridCatalogProfile TrackerAccess = new(
        ["private", "public", "seed", "leech", "scrape"],
        ["userid", "private", "seed", "leech", "scrape", "version"],
        [new("userid", GridSortDirection.Asc)]);

    public static readonly GridCatalogProfile Bans = new(
        ["active", "expired"],
        ["scope", "subject", "expires"],
        [new("scope", GridSortDirection.Asc), new("subject", GridSortDirection.Asc)]);

    public static readonly GridCatalogProfile TrackerNodes = new(
        [],
        ["nodekey", "nodename", "environment", "region", "updated"],
        [new("nodekey", GridSortDirection.Asc)]);
}
