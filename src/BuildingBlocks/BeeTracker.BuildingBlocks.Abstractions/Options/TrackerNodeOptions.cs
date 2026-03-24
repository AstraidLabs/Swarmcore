namespace BeeTracker.BuildingBlocks.Abstractions.Options;

public sealed class TrackerNodeOptions
{
    public const string SectionName = "BeeTracker:TrackerNode";

    public string NodeId { get; init; } = Environment.MachineName;
    public string Region { get; init; } = "local";
}
