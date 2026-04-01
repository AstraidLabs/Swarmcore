using Microsoft.Extensions.Options;
using BeeTracker.BuildingBlocks.Abstractions.Options;
using Tracker.Gateway.Application.Announce;
using Tracker.Gateway.Infrastructure;

namespace Tracker.Gateway.UnitTests;

public sealed class AbuseEventEmissionTests
{
    private readonly CollectingEventWriter _writer = new();

    private AdvancedAbuseGuard CreateGuard(string nodeId = "test-node")
    {
        return new AdvancedAbuseGuard(
            _writer,
            Options.Create(new TrackerNodeOptions { NodeId = nodeId }));
    }

    [Fact]
    public void RecordMalformedRequest_EmitsEvent()
    {
        var guard = CreateGuard();
        guard.RecordMalformedRequest("10.0.0.1", "pk1");

        Assert.Single(_writer.Events);
        var evt = _writer.Events[0];
        Assert.Equal("10.0.0.1", evt.Ip);
        Assert.Equal("pk1", evt.Passkey);
        Assert.Equal(AbuseEventTypes.MalformedRequest, evt.EventType);
        Assert.Equal("test-node", evt.NodeId);
    }

    [Fact]
    public void RecordDeniedPolicy_EmitsEvent()
    {
        var guard = CreateGuard();
        guard.RecordDeniedPolicy("10.0.0.2", "pk2");

        Assert.Single(_writer.Events);
        Assert.Equal(AbuseEventTypes.DeniedPolicy, _writer.Events[0].EventType);
    }

    [Fact]
    public void RecordPeerIdAnomaly_EmitsEvent()
    {
        var guard = CreateGuard();
        guard.RecordPeerIdAnomaly("10.0.0.3", null);

        Assert.Single(_writer.Events);
        Assert.Equal(AbuseEventTypes.PeerIdAnomaly, _writer.Events[0].EventType);
        Assert.Null(_writer.Events[0].Passkey);
    }

    [Fact]
    public void RecordSuspiciousPattern_EmitsEvent()
    {
        var guard = CreateGuard();
        guard.RecordSuspiciousPattern("10.0.0.4", "pk4");

        Assert.Single(_writer.Events);
        Assert.Equal(AbuseEventTypes.SuspiciousPattern, _writer.Events[0].EventType);
    }

    [Fact]
    public void RecordScrapeAmplification_EmitsEvent()
    {
        var guard = CreateGuard();
        guard.RecordScrapeAmplification("10.0.0.5");

        Assert.Single(_writer.Events);
        Assert.Equal(AbuseEventTypes.ScrapeAmplification, _writer.Events[0].EventType);
        Assert.Null(_writer.Events[0].Passkey);
    }

    [Fact]
    public void MultipleRecords_EmitMultipleEvents()
    {
        var guard = CreateGuard();
        guard.RecordMalformedRequest("10.0.0.6", null);
        guard.RecordDeniedPolicy("10.0.0.6", "pk6");
        guard.RecordPeerIdAnomaly("10.0.0.6", null);

        Assert.Equal(3, _writer.Events.Count);
    }

    [Fact]
    public void ParameterlessConstructor_DoesNotEmitEvents()
    {
        var guard = new AdvancedAbuseGuard();
        guard.RecordMalformedRequest("10.0.0.7", null);

        // No exception thrown, no writer to collect from
        Assert.Empty(_writer.Events);
    }

    [Fact]
    public void NodeId_UsedInEvents()
    {
        var guard = CreateGuard("eu-west-42");
        guard.RecordMalformedRequest("10.0.0.8", null);

        Assert.Equal("eu-west-42", _writer.Events[0].NodeId);
    }

    [Fact]
    public void Events_HaveUniqueIds()
    {
        var guard = CreateGuard();
        guard.RecordMalformedRequest("10.0.0.9", null);
        guard.RecordMalformedRequest("10.0.0.9", null);

        Assert.NotEqual(_writer.Events[0].Id, _writer.Events[1].Id);
    }

    [Fact]
    public void Events_HaveScoreContribution()
    {
        var guard = CreateGuard();
        guard.RecordMalformedRequest("10.0.0.10", null);

        Assert.True(_writer.Events[0].ScoreContribution > 0);
    }

    private sealed class CollectingEventWriter : IAbuseEventChannelWriter
    {
        public List<AbuseEvent> Events { get; } = [];

        public bool TryWrite(AbuseEvent abuseEvent)
        {
            Events.Add(abuseEvent);
            return true;
        }
    }
}
