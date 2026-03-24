namespace BeeTracker.BuildingBlocks.Abstractions.Options;

public sealed class TelemetryBatchingOptions
{
    public const string SectionName = "BeeTracker:TelemetryBatching";

    public int BatchSize { get; init; } = 250;
    public int FlushIntervalMilliseconds { get; init; } = 1000;
}
