using NSubstitute;
using BeeTracker.Contracts.Admin;
using Tracker.AdminService.Application;

namespace Tracker.AdminService.UnitTests;

public sealed class GetDashboardSummaryQueryHandlerTests
{
    private readonly IClusterOverviewReader _clusterOverviewReader = Substitute.For<IClusterOverviewReader>();
    private readonly IClusterNodeStateReader _nodeStateReader = Substitute.For<IClusterNodeStateReader>();
    private readonly INotificationAdminReader _notificationReader = Substitute.For<INotificationAdminReader>();

    private GetDashboardSummaryQueryHandler CreateHandler()
        => new(_clusterOverviewReader, _nodeStateReader, _notificationReader);

    [Fact]
    public async Task Handle_ReturnsAggregatedSummary_WithCorrectCounts()
    {
        var now = DateTimeOffset.UtcNow;
        var overview = new ClusterOverviewDto(
            now,
            ActiveNodeCount: 3,
            Nodes: new[]
            {
                new NodeHealthDto("node-1", "eu-west", Ready: true, now),
                new NodeHealthDto("node-2", "eu-west", Ready: true, now),
                new NodeHealthDto("node-3", "us-east", Ready: false, now)
            });

        var nodeStates = new[]
        {
            new ClusterNodeStateDto("node-1", "eu-west", "Active", OwnedShardCount: 85, now, HeartbeatFresh: true),
            new ClusterNodeStateDto("node-2", "eu-west", "Active", OwnedShardCount: 86, now, HeartbeatFresh: true),
            new ClusterNodeStateDto("node-3", "us-east", "Draining", OwnedShardCount: 85, now, HeartbeatFresh: false)
        };

        var notificationStats = new NotificationOutboxStatsDto(
            PendingCount: 5,
            ProcessingCount: 2,
            SentCount: 100,
            FailedCount: 3,
            CancelledCount: 1,
            TotalCount: 111);

        _clusterOverviewReader.GetAsync(Arg.Any<CancellationToken>()).Returns(overview);
        _nodeStateReader.GetAllNodeStatesAsync(Arg.Any<CancellationToken>()).Returns(nodeStates);
        _notificationReader.GetStatsAsync(Arg.Any<CancellationToken>()).Returns(notificationStats);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardSummaryQuery(), CancellationToken.None);

        Assert.Equal(3, result.ActiveNodeCount);
        Assert.Equal(2, result.ReadyNodeCount);
        Assert.Equal(1, result.DegradedNodeCount);
        Assert.Equal(256, result.TotalOwnedShards);
        Assert.Equal(5, result.NotificationStats.PendingCount);
        Assert.Equal(3, result.NotificationStats.FailedCount);
        Assert.Equal(3, result.NodeStates.Count);
    }

    [Fact]
    public async Task Handle_WithNoNodes_ReturnsZeroCounts()
    {
        var now = DateTimeOffset.UtcNow;
        var overview = new ClusterOverviewDto(now, ActiveNodeCount: 0, Nodes: Array.Empty<NodeHealthDto>());
        var notificationStats = new NotificationOutboxStatsDto(0, 0, 0, 0, 0, 0);

        _clusterOverviewReader.GetAsync(Arg.Any<CancellationToken>()).Returns(overview);
        _nodeStateReader.GetAllNodeStatesAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ClusterNodeStateDto>());
        _notificationReader.GetStatsAsync(Arg.Any<CancellationToken>()).Returns(notificationStats);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardSummaryQuery(), CancellationToken.None);

        Assert.Equal(0, result.ActiveNodeCount);
        Assert.Equal(0, result.ReadyNodeCount);
        Assert.Equal(0, result.DegradedNodeCount);
        Assert.Equal(0, result.TotalOwnedShards);
        Assert.Empty(result.NodeStates);
    }

    [Fact]
    public async Task Handle_AllNodesReady_DegradedCountIsZero()
    {
        var now = DateTimeOffset.UtcNow;
        var overview = new ClusterOverviewDto(
            now,
            ActiveNodeCount: 2,
            Nodes: new[]
            {
                new NodeHealthDto("node-1", "eu-west", Ready: true, now),
                new NodeHealthDto("node-2", "eu-west", Ready: true, now)
            });

        var nodeStates = new[]
        {
            new ClusterNodeStateDto("node-1", "eu-west", "Active", 128, now, true),
            new ClusterNodeStateDto("node-2", "eu-west", "Active", 128, now, true)
        };

        var notificationStats = new NotificationOutboxStatsDto(0, 0, 50, 0, 0, 50);

        _clusterOverviewReader.GetAsync(Arg.Any<CancellationToken>()).Returns(overview);
        _nodeStateReader.GetAllNodeStatesAsync(Arg.Any<CancellationToken>()).Returns(nodeStates);
        _notificationReader.GetStatsAsync(Arg.Any<CancellationToken>()).Returns(notificationStats);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardSummaryQuery(), CancellationToken.None);

        Assert.Equal(2, result.ReadyNodeCount);
        Assert.Equal(0, result.DegradedNodeCount);
        Assert.Equal(256, result.TotalOwnedShards);
    }
}
