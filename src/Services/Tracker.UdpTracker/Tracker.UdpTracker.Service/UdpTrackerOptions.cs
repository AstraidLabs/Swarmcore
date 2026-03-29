namespace Tracker.UdpTracker.Service;

public sealed class UdpTrackerOptions
{
    public const string SectionName = "BeeTracker:UdpTracker";

    public bool Enabled { get; init; } = false;
    public string BindAddress { get; init; } = "0.0.0.0";
    public int Port { get; init; } = 6969;
    public int ConnectionTimeoutSeconds { get; init; } = 120;
    public int ReceiveBufferSize { get; init; } = 65536;
    public int MaxDatagramSize { get; init; } = 1500;
    public bool EnableScrape { get; init; } = true;
    public int MaxScrapeInfoHashes { get; init; } = 32;
    public int ConnectionIdSweepIntervalSeconds { get; init; } = 30;
}
