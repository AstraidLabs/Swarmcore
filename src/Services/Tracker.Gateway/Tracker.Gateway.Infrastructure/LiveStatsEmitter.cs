using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using BeeTracker.BuildingBlocks.Abstractions.Options;
using BeeTracker.Caching.Redis;
using Tracker.Gateway.Application.Announce;
using Tracker.Gateway.Runtime;

namespace Tracker.Gateway.Infrastructure;

public sealed class LiveStatsEmitter : BackgroundService
{
    private readonly IRedisCacheClient _redisCacheClient;
    private readonly IRuntimeSwarmStore _runtimeSwarmStore;
    private readonly IAnnounceTelemetryWriter _telemetryWriter;
    private readonly TrackerNodeOptions _nodeOptions;
    private readonly ILogger<LiveStatsEmitter> _logger;

    public LiveStatsEmitter(
        IRedisCacheClient redisCacheClient,
        IRuntimeSwarmStore runtimeSwarmStore,
        IAnnounceTelemetryWriter telemetryWriter,
        IOptions<TrackerNodeOptions> nodeOptions,
        ILogger<LiveStatsEmitter> logger)
    {
        _redisCacheClient = redisCacheClient;
        _runtimeSwarmStore = runtimeSwarmStore;
        _telemetryWriter = telemetryWriter;
        _nodeOptions = nodeOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        const string channelName = "tracker:live-stats";

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

                var store = (PartitionedRuntimeSwarmStore)_runtimeSwarmStore;
                var stats = new
                {
                    nodeId = _nodeOptions.NodeId,
                    activePeers = store.GetTotalPeerCount(),
                    activeSwarms = store.GetTotalSwarmCount(),
                    telemetryQueueLength = _telemetryWriter.QueueLength,
                    timestamp = DateTimeOffset.UtcNow
                };

                var json = JsonSerializer.Serialize(stats);
                await _redisCacheClient.Subscriber.PublishAsync(RedisChannel.Literal(channelName), json);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish live stats.");
            }
        }
    }
}
