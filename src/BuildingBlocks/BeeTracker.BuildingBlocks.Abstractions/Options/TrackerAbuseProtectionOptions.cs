namespace BeeTracker.BuildingBlocks.Abstractions.Options;

public sealed class TrackerAbuseProtectionOptions
{
    public const string SectionName = "BeeTracker:TrackerAbuseProtection";

    public bool EnableAnnouncePasskeyRateLimit { get; init; } = true;
    public int AnnouncePerPasskeyPerSecond { get; init; } = 30;
    public bool EnableAnnounceIpRateLimit { get; init; } = false;
    public int AnnouncePerIpPerSecond { get; init; } = 60;
    public bool EnableScrapeIpRateLimit { get; init; } = true;
    public int ScrapePerIpPerSecond { get; init; } = 10;
}
