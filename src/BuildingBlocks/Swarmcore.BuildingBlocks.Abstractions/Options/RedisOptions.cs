namespace Swarmcore.BuildingBlocks.Abstractions.Options;

public sealed class RedisOptions
{
    public const string SectionName = "Swarmcore:Redis";

    public string Configuration { get; init; } = "localhost:6379";
}
