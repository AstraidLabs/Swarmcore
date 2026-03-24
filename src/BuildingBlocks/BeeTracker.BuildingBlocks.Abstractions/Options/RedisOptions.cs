namespace BeeTracker.BuildingBlocks.Abstractions.Options;

public sealed class RedisOptions
{
    public const string SectionName = "BeeTracker:Redis";

    public string Configuration { get; init; } = "localhost:6379";
}
