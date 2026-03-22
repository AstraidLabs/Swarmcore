using Swarmcore.Contracts.Telemetry;

namespace Tracker.TelemetryIngest.Application;

public interface ITelemetryBatchSink
{
    Task PersistBatchAsync(IReadOnlyCollection<AnnounceTelemetryEvent> batch, CancellationToken cancellationToken);
}

public interface ITelemetryBuffer
{
    ValueTask QueueAsync(AnnounceTelemetryEvent telemetryEvent, CancellationToken cancellationToken);
    IAsyncEnumerable<IReadOnlyCollection<AnnounceTelemetryEvent>> ReadBatchesAsync(CancellationToken cancellationToken);
}
