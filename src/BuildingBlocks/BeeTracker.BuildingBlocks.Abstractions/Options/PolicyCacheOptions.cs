namespace BeeTracker.BuildingBlocks.Abstractions.Options;

public sealed class PolicyCacheOptions
{
    public const string SectionName = "BeeTracker:PolicyCache";

    public int L1Seconds { get; init; } = 15;
    public int L2Seconds { get; init; } = 120;
}
