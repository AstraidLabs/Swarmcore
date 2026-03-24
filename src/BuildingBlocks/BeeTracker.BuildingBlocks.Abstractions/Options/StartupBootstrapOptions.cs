namespace BeeTracker.BuildingBlocks.Abstractions.Options;

public sealed class StartupBootstrapOptions
{
    public const string SectionName = "BeeTracker:StartupBootstrap";

    public int MaxAttempts { get; init; } = 20;

    public int RetryDelaySeconds { get; init; } = 3;
}
