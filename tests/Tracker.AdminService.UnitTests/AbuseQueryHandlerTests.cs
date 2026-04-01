using NSubstitute;
using BeeTracker.Contracts.Admin;
using Tracker.AdminService.Application;

namespace Tracker.AdminService.UnitTests;

public sealed class AbuseQueryHandlerTests
{
    private readonly IAbuseEventReader _reader = Substitute.For<IAbuseEventReader>();

    // ─── ListAbuseEventsQueryHandler ──────────────────────────────────────────

    [Fact]
    public async Task ListAbuseEvents_DelegatesToReader()
    {
        var expected = new AbuseEventFeedResultDto([], 0);
        _reader.ListAsync(1, 25, null, null, Arg.Any<CancellationToken>()).Returns(expected);

        var handler = new ListAbuseEventsQueryHandler(_reader);
        var result = await handler.Handle(new ListAbuseEventsQuery(1, 25), CancellationToken.None);

        Assert.Same(expected, result);
        await _reader.Received(1).ListAsync(1, 25, null, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAbuseEvents_PassesIpFilter()
    {
        var expected = new AbuseEventFeedResultDto([], 0);
        _reader.ListAsync(1, 10, "10.0.0.1", null, Arg.Any<CancellationToken>()).Returns(expected);

        var handler = new ListAbuseEventsQueryHandler(_reader);
        var result = await handler.Handle(new ListAbuseEventsQuery(1, 10, Ip: "10.0.0.1"), CancellationToken.None);

        Assert.Same(expected, result);
        await _reader.Received(1).ListAsync(1, 10, "10.0.0.1", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAbuseEvents_PassesEventTypeFilter()
    {
        var expected = new AbuseEventFeedResultDto([], 0);
        _reader.ListAsync(2, 50, null, "malformed_request", Arg.Any<CancellationToken>()).Returns(expected);

        var handler = new ListAbuseEventsQueryHandler(_reader);
        var result = await handler.Handle(
            new ListAbuseEventsQuery(2, 50, EventType: "malformed_request"), CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task ListAbuseEvents_ReturnsItemsFromReader()
    {
        var items = new[]
        {
            new AbuseEventDto(Guid.NewGuid(), "node-1", "10.0.0.1", "pk1",
                "malformed_request", 5, null, DateTimeOffset.UtcNow)
        };
        var expected = new AbuseEventFeedResultDto(items, 1);
        _reader.ListAsync(1, 25, null, null, Arg.Any<CancellationToken>()).Returns(expected);

        var handler = new ListAbuseEventsQueryHandler(_reader);
        var result = await handler.Handle(new ListAbuseEventsQuery(1, 25), CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
    }

    // ─── GetAbuseOverviewQueryHandler ─────────────────────────────────────────

    [Fact]
    public async Task GetAbuseOverview_DelegatesToReader()
    {
        var expected = new AbuseOverviewDto(0, 0, 0, [], DateTimeOffset.UtcNow);
        _reader.GetOverviewAsync(50, Arg.Any<CancellationToken>()).Returns(expected);

        var handler = new GetAbuseOverviewQueryHandler(_reader);
        var result = await handler.Handle(new GetAbuseOverviewQuery(), CancellationToken.None);

        Assert.Same(expected, result);
        await _reader.Received(1).GetOverviewAsync(50, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAbuseOverview_PassesCustomTopOffenderCount()
    {
        var expected = new AbuseOverviewDto(10, 5, 3, [], DateTimeOffset.UtcNow);
        _reader.GetOverviewAsync(20, Arg.Any<CancellationToken>()).Returns(expected);

        var handler = new GetAbuseOverviewQueryHandler(_reader);
        var result = await handler.Handle(new GetAbuseOverviewQuery(20), CancellationToken.None);

        Assert.Same(expected, result);
        await _reader.Received(1).GetOverviewAsync(20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAbuseOverview_ReturnsTopOffenders()
    {
        var offenders = new[]
        {
            new AggregatedAbuseOffenderDto("10.0.0.1", 100, 500, "HardBlock",
                DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow),
            new AggregatedAbuseOffenderDto("10.0.0.2", 50, 200, "SoftRestrict",
                DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow)
        };
        var expected = new AbuseOverviewDto(150, 2, 1, offenders, DateTimeOffset.UtcNow);
        _reader.GetOverviewAsync(50, Arg.Any<CancellationToken>()).Returns(expected);

        var handler = new GetAbuseOverviewQueryHandler(_reader);
        var result = await handler.Handle(new GetAbuseOverviewQuery(), CancellationToken.None);

        Assert.Equal(2, result.TopOffenders.Count);
        Assert.Equal(150, result.TotalEvents);
        Assert.Equal(2, result.DistinctIps);
    }
}
