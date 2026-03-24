namespace BeeTracker.BuildingBlocks.Abstractions.Options;

public sealed class DependencyDegradationOptions
{
    public const string SectionName = "BeeTracker:DependencyDegradation";

    public bool RequireRedisForReadiness { get; init; } = false;
    public bool RequirePostgresForReadiness { get; init; } = false;
}
