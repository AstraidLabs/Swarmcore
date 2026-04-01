using BeeTracker.Contracts.Configuration;
using Tracker.Gateway.Application.Announce;

namespace Tracker.Gateway.UnitTests;

public sealed class AbuseEventContractTests
{
    [Fact]
    public void TrackerBanScopes_IncludesIp()
    {
        Assert.Equal("ip", TrackerBanScopes.Ip);
    }

    [Fact]
    public void AbuseEventTypes_AllConstantsAreDefined()
    {
        Assert.Equal("malformed_request", AbuseEventTypes.MalformedRequest);
        Assert.Equal("denied_policy", AbuseEventTypes.DeniedPolicy);
        Assert.Equal("peer_id_anomaly", AbuseEventTypes.PeerIdAnomaly);
        Assert.Equal("suspicious_pattern", AbuseEventTypes.SuspiciousPattern);
        Assert.Equal("scrape_amplification", AbuseEventTypes.ScrapeAmplification);
    }

    [Fact]
    public void AbuseEvent_RecordConstruction_Roundtrips()
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var evt = new AbuseEvent(id, "node-1", "10.0.0.1", "pk1",
            AbuseEventTypes.MalformedRequest, 5, "test detail", now);

        Assert.Equal(id, evt.Id);
        Assert.Equal("node-1", evt.NodeId);
        Assert.Equal("10.0.0.1", evt.Ip);
        Assert.Equal("pk1", evt.Passkey);
        Assert.Equal(AbuseEventTypes.MalformedRequest, evt.EventType);
        Assert.Equal(5, evt.ScoreContribution);
        Assert.Equal("test detail", evt.Detail);
        Assert.Equal(now, evt.OccurredAtUtc);
    }

    [Fact]
    public void AbuseEvent_NullPasskey_IsAllowed()
    {
        var evt = new AbuseEvent(Guid.NewGuid(), "node-1", "10.0.0.1", null,
            AbuseEventTypes.ScrapeAmplification, 3, null, DateTimeOffset.UtcNow);

        Assert.Null(evt.Passkey);
        Assert.Null(evt.Detail);
    }
}
