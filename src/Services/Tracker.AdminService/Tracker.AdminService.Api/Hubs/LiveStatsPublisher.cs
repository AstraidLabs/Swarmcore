using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using BeeTracker.Caching.Redis;

namespace Tracker.AdminService.Api.Hubs;

public sealed class LiveStatsPublisher : BackgroundService
{
    private readonly IRedisCacheClient _redisCacheClient;
    private readonly IHubContext<LiveStatsHub> _hubContext;
    private readonly ILogger<LiveStatsPublisher> _logger;

    public LiveStatsPublisher(
        IRedisCacheClient redisCacheClient,
        IHubContext<LiveStatsHub> hubContext,
        ILogger<LiveStatsPublisher> logger)
    {
        _redisCacheClient = redisCacheClient;
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
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
                await _hubContext.Clients.All.SendAsync("StatsUpdate", (string)message!, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to push live stats update to SignalR clients.");
            }
        });

        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
