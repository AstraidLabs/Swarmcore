namespace Swarmcore.BuildingBlocks.Abstractions.Options;

public sealed class StartupBootstrapOptions
{
    public const string SectionName = "Swarmcore:StartupBootstrap";

    public int MaxAttempts { get; init; } = 20;

    public int RetryDelaySeconds { get; init; } = 3;
}
