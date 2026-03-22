namespace Swarmcore.BuildingBlocks.Abstractions.Options;

public sealed class TrackerNodeOptions
{
    public const string SectionName = "Swarmcore:TrackerNode";

    public string NodeId { get; init; } = Environment.MachineName;
    public string Region { get; init; } = "local";
}
