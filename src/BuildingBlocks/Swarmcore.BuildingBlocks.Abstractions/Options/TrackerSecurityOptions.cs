namespace Swarmcore.BuildingBlocks.Abstractions.Options;

public sealed class TrackerSecurityOptions
{
    public const string SectionName = "Swarmcore:TrackerSecurity";

    public int AnnounceMaxQueryLength { get; init; } = 2048;
    public int ScrapeMaxQueryLength { get; init; } = 4096;
    public int HardMaxNumWant { get; init; } = 200;
    public bool RequireCompactResponses { get; init; } = true;
    public bool AllowPasskeyInQueryString { get; init; } = false;
    public bool AllowIPv6Peers { get; init; } = false;
    public int MaxScrapeInfoHashes { get; init; } = 74;
}
