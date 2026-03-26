using BeeTracker.Contracts.Configuration;

namespace Tracker.Gateway.UnitTests;

#pragma warning disable CS0618
public sealed class TrackerAccessRightsTests
{
    [Fact]
    public void EvaluateAnnounce_ReturnsPrivateTrackerDenied_WhenPrivateAccessIsDisabled()
    {
        var rights = new TrackerAccessRightsDto(Guid.NewGuid(), CanLeech: true, CanSeed: true, CanScrape: true, CanUsePrivateTracker: false, Version: 3);

        var decision = rights.EvaluateAnnounce(isSeeder: false);

        Assert.False(decision.IsAllowed);
        Assert.Equal(TrackerAccessDenialReasons.PrivateTrackerAccessDenied, decision.FailureReason);
    }

    [Fact]
    public void EvaluateAnnounce_ReturnsSeederDenied_WhenSeederRightIsMissing()
    {
        var rights = new TrackerAccessRightsDto(Guid.NewGuid(), CanLeech: true, CanSeed: false, CanScrape: true, CanUsePrivateTracker: true, Version: 5);

        var decision = rights.EvaluateAnnounce(isSeeder: true);

        Assert.False(decision.IsAllowed);
        Assert.Equal(TrackerAccessDenialReasons.SeedingNotPermitted, decision.FailureReason);
    }

    [Fact]
    public void EvaluateScrape_ReturnsScrapeDenied_WhenScrapeRightIsMissing()
    {
        var rights = new TrackerAccessRightsDto(Guid.NewGuid(), CanLeech: true, CanSeed: true, CanScrape: false, CanUsePrivateTracker: true, Version: 7);

        var decision = rights.EvaluateScrape();

        Assert.False(decision.IsAllowed);
        Assert.Equal(TrackerAccessDenialReasons.ScrapeNotPermitted, decision.FailureReason);
    }

    [Fact]
    public void SnapshotConversions_PreserveTrackerAccessFlags()
    {
        var snapshot = new UserPermissionSnapshotDto(Guid.NewGuid(), CanLeech: true, CanSeed: false, CanScrape: true, CanUsePrivateTracker: true, Version: 11);

        var rights = TrackerAccessRights.FromSnapshot(snapshot);
        var roundTrip = rights.ToSnapshot();

        Assert.Equal(snapshot.UserId, rights.UserId);
        Assert.Equal(snapshot.CanUsePrivateTracker, rights.CanUsePrivateTracker);
        Assert.Equal(snapshot.CanSeed, rights.CanSeed);
        Assert.Equal(snapshot, roundTrip);
    }
}
#pragma warning restore CS0618
