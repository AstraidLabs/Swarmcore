using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using Swarmcore.Contracts.Telemetry;
using Swarmcore.Persistence.Postgres;
using Tracker.TelemetryIngest.Application;

namespace Tracker.TelemetryIngest.Infrastructure;

public sealed class ChannelTelemetryBuffer(IOptions<TelemetryBatchingOptions> options) : ITelemetryBuffer
{
    private readonly Channel<AnnounceTelemetryEvent> _channel = Channel.CreateUnbounded<AnnounceTelemetryEvent>();
    private readonly TelemetryBatchingOptions _options = options.Value;

    public ValueTask QueueAsync(AnnounceTelemetryEvent telemetryEvent, CancellationToken cancellationToken)
        => _channel.Writer.WriteAsync(telemetryEvent, cancellationToken);

    public async IAsyncEnumerable<IReadOnlyCollection<AnnounceTelemetryEvent>> ReadBatchesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = new List<AnnounceTelemetryEvent>(_options.BatchSize);

        while (!cancellationToken.IsCancellationRequested)
        {
            while (buffer.Count < _options.BatchSize && await _channel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (buffer.Count < _options.BatchSize && _channel.Reader.TryRead(out var item))
                {
                    buffer.Add(item);
                }

                if (buffer.Count > 0)
                {
                    break;
                }
            }

            if (buffer.Count == 0)
            {
                continue;
            }

            yield return buffer.ToArray();
            buffer.Clear();
        }
    }
}

public sealed class PostgresTelemetryBatchSink(IPostgresConnectionFactory connectionFactory) : ITelemetryBatchSink
{
    public async Task PersistBatchAsync(IReadOnlyCollection<AnnounceTelemetryEvent> batch, CancellationToken cancellationToken)
    {
        const string sql = """
            insert into announce_telemetry (node_id, info_hash, passkey, event_name, requested_peers, occurred_at_utc)
            values (@NodeId, @InfoHash, @Passkey, @EventName, @RequestedPeers, @OccurredAtUtc)
            """;

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, batch, cancellationToken: cancellationToken));
    }
}

public static class TelemetryInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddTelemetryInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<TelemetryDbContext>((serviceProvider, options) =>
        {
            var postgresOptions = serviceProvider.GetRequiredService<IOptions<PostgresOptions>>().Value;
            options.UseNpgsql(postgresOptions.ConnectionString);
        });
        services.AddSingleton<ITelemetryBuffer, ChannelTelemetryBuffer>();
        services.AddSingleton<ITelemetryBatchSink, PostgresTelemetryBatchSink>();
        return services;
    }
}
