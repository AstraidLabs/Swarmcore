using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using BeeTracker.Caching.Redis;
using BeeTracker.Contracts.Admin;
using MediatR;
using Tracker.AdminService.Application;

namespace Tracker.AdminService.Api.Hubs;

public sealed class LiveStatsPublisher : BackgroundService
{
    private readonly IRedisCacheClient _redisCacheClient;
    private readonly IHubContext<LiveStatsHub> _hubContext;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LiveStatsPublisher> _logger;

    private static readonly TimeSpan SummaryInterval = TimeSpan.FromSeconds(15);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public LiveStatsPublisher(
        IRedisCacheClient redisCacheClient,
        IHubContext<LiveStatsHub> hubContext,
        IServiceScopeFactory scopeFactory,
        ILogger<LiveStatsPublisher> logger)
    {
        _redisCacheClient = redisCacheClient;
        _hubContext = hubContext;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriptionTask = SubscribeToLiveStatsAsync(stoppingToken);
        var summaryTask = PublishDashboardSummaryLoopAsync(stoppingToken);

        await Task.WhenAll(subscriptionTask, summaryTask);
    }

    private async Task SubscribeToLiveStatsAsync(CancellationToken stoppingToken)
    {
        const string channelName = "tracker:live-stats";

        await _redisCacheClient.Subscriber.SubscribeAsync(RedisChannel.Literal(channelName), async (_, message) =>
        {
            if (message.IsNullOrEmpty)
            {
                return;
            }

            try
            {
                await _hubContext.Clients.Group(LiveStatsHub.DashboardGroup)
                    .SendAsync("StatsUpdate", (string)message!, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to push live stats update to SignalR clients.");
            }
        });

        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }

    private async Task PublishDashboardSummaryLoopAsync(CancellationToken stoppingToken)
    {
        // Allow the application to fully start before the first summary broadcast.
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();
                var summary = await sender.Send(new GetDashboardSummaryQuery(), stoppingToken);

                var json = JsonSerializer.Serialize(summary, JsonOptions);
                await _hubContext.Clients.Group(LiveStatsHub.DashboardGroup)
                    .SendAsync("DashboardSummary", json, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish dashboard summary to SignalR clients.");
            }

            await Task.Delay(SummaryInterval, stoppingToken);
        }
    }
}
