namespace BeeTracker.BuildingBlocks.Abstractions.Options;

public sealed class TrackerNodeOptions
{
    public const string SectionName = "BeeTracker:TrackerNode";

    public string NodeId { get; init; } = System.Environment.MachineName;
    public string NodeName { get; init; } = System.Environment.MachineName;
    public string Environment { get; init; } = "development";
    public string Region { get; init; } = "local";
    public string PublicBaseUrl { get; init; } = "http://localhost:8080";
    public string InternalBaseUrl { get; init; } = "http://localhost:8080";
    public string AnnounceRoute { get; init; } = "/announce/{passkey?}";
    public string PrivateAnnounceRoute { get; init; } = "/announce/private/{passkey?}";
    public string ScrapeRoute { get; init; } = "/scrape/{passkey?}";
    public int DefaultAnnounceIntervalSeconds { get; init; } = 1800;
    public int MinAnnounceIntervalSeconds { get; init; } = 900;
    public int DefaultNumWant { get; init; } = 50;
    public bool EnableHealthEndpoints { get; init; } = true;
    public bool EnableMetrics { get; init; } = true;
    public bool EnableTracing { get; init; } = false;
    public bool EnableDiagnosticsEndpoints { get; init; } = true;
    public string LiveHealthRoute { get; init; } = "/health/live";
    public string ReadyHealthRoute { get; init; } = "/health/ready";
    public string StartupHealthRoute { get; init; } = "/health/startup";
}
