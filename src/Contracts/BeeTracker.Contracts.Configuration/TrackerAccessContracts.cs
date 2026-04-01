namespace BeeTracker.Contracts.Configuration;

public static class TrackerBanScopes
{
    public const string Torrent = "torrent";
    public const string Passkey = "passkey";
    public const string User = "user";
    public const string Ip = "ip";
}

public static class TrackerAccessDenialReasons
{
    public const string PermissionsUnavailable = "permissions unavailable";
    public const string PrivateTrackerAccessDenied = "private tracker access denied";
    public const string SeedingNotPermitted = "seeding is not permitted";
    public const string LeechingNotPermitted = "leeching is not permitted";
    public const string ScrapeNotPermitted = "scrape is not permitted";
}

public readonly record struct TrackerAccessDecision(bool IsAllowed, string? FailureReason)
{
    public static TrackerAccessDecision Allow() => new(true, null);
    public static TrackerAccessDecision Deny(string failureReason) => new(false, failureReason);
}

public sealed record TrackerAccessRightsDto(
    Guid UserId,
    bool CanLeech,
    bool CanSeed,
    bool CanScrape,
    bool CanUsePrivateTracker,
    long Version);

public sealed record TrackerAccessRightsUpsertRequest(
    bool CanLeech,
    bool CanSeed,
    bool CanScrape,
    bool CanUsePrivateTracker,
    long? ExpectedVersion = null);

public sealed record BulkTrackerAccessRightsUpsertItem(
    Guid UserId,
    bool CanLeech,
    bool CanSeed,
    bool CanScrape,
    bool CanUsePrivateTracker,
    long? ExpectedVersion = null);

#pragma warning disable CS0618
public static class TrackerAccessRights
{
    public static TrackerAccessRightsDto FromSnapshot(UserPermissionSnapshotDto snapshot)
        => new(
            snapshot.UserId,
            snapshot.CanLeech,
            snapshot.CanSeed,
            snapshot.CanScrape,
            snapshot.CanUsePrivateTracker,
            snapshot.Version);

    public static UserPermissionSnapshotDto ToSnapshot(this TrackerAccessRightsDto rights)
        => new(
            rights.UserId,
            rights.CanLeech,
            rights.CanSeed,
            rights.CanScrape,
            rights.CanUsePrivateTracker,
            rights.Version);

    public static TrackerAccessRightsUpsertRequest ToTrackerAccessRightsRequest(this UserPermissionUpsertRequest request)
        => new(
            request.CanLeech,
            request.CanSeed,
            request.CanScrape,
            request.CanUsePrivateTracker,
            request.ExpectedVersion);

    public static UserPermissionUpsertRequest ToUserPermissionRequest(this TrackerAccessRightsUpsertRequest request)
        => new(
            request.CanLeech,
            request.CanSeed,
            request.CanScrape,
            request.CanUsePrivateTracker,
            request.ExpectedVersion);

    public static BulkTrackerAccessRightsUpsertItem ToTrackerAccessRightsItem(this BulkUserPermissionUpsertItem item)
        => new(
            item.UserId,
            item.CanLeech,
            item.CanSeed,
            item.CanScrape,
            item.CanUsePrivateTracker,
            item.ExpectedVersion);

    public static BulkUserPermissionUpsertItem ToUserPermissionItem(this BulkTrackerAccessRightsUpsertItem item)
        => new(
            item.UserId,
            item.CanLeech,
            item.CanSeed,
            item.CanScrape,
            item.CanUsePrivateTracker,
            item.ExpectedVersion);

    public static TrackerAccessDecision EvaluateAnnounce(this TrackerAccessRightsDto? rights, bool isSeeder)
    {
        if (rights is null)
        {
            return TrackerAccessDecision.Deny(TrackerAccessDenialReasons.PermissionsUnavailable);
        }

        if (!rights.CanUsePrivateTracker)
        {
            return TrackerAccessDecision.Deny(TrackerAccessDenialReasons.PrivateTrackerAccessDenied);
        }

        if (isSeeder && !rights.CanSeed)
        {
            return TrackerAccessDecision.Deny(TrackerAccessDenialReasons.SeedingNotPermitted);
        }

        if (!isSeeder && !rights.CanLeech)
        {
            return TrackerAccessDecision.Deny(TrackerAccessDenialReasons.LeechingNotPermitted);
        }

        return TrackerAccessDecision.Allow();
    }

    public static TrackerAccessDecision EvaluateScrape(this TrackerAccessRightsDto? rights)
    {
        if (rights is null)
        {
            return TrackerAccessDecision.Deny(TrackerAccessDenialReasons.PermissionsUnavailable);
        }

        if (!rights.CanUsePrivateTracker)
        {
            return TrackerAccessDecision.Deny(TrackerAccessDenialReasons.PrivateTrackerAccessDenied);
        }

        if (!rights.CanScrape)
        {
            return TrackerAccessDecision.Deny(TrackerAccessDenialReasons.ScrapeNotPermitted);
        }

        return TrackerAccessDecision.Allow();
    }
}
#pragma warning restore CS0618
