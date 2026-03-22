namespace Swarmcore.BuildingBlocks.Abstractions.Telemetry;

public interface ITelemetryChannelWriter<in T>
{
    bool TryWrite(T item);
    ValueTask WriteAsync(T item, CancellationToken cancellationToken);
}
