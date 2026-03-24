namespace BeeTracker.BuildingBlocks.Abstractions.Options;

public sealed class TrackerSecurityOptions
{
    public const string SectionName = "BeeTracker:TrackerSecurity";

    public int AnnounceMaxQueryLength { get; init; } = 2048;
    public int ScrapeMaxQueryLength { get; init; } = 4096;
    public int HardMaxNumWant { get; init; } = 200;
    public bool RequireCompactResponses { get; init; } = true;
    public bool AllowPasskeyInQueryString { get; init; } = false;
    public bool AllowIPv6Peers { get; init; } = false;
    public int MaxScrapeInfoHashes { get; init; } = 74;

    /// <summary>
    /// When true, the tracker honors the 'ip' query parameter from announce requests,
    /// allowing clients to specify their own IP address. This is a security-sensitive
    /// setting and should only be enabled when the tracker is behind a trusted proxy
    /// that does not preserve the original client IP, or for local network deployments.
    /// Default: false.
    /// </summary>
    public bool AllowClientIpOverride { get; init; } = false;
}
