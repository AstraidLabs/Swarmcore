namespace Tracker.UdpTracker.Service;

public sealed class UdpTrackerOptions
{
    public const string SectionName = "BeeTracker:UdpTracker";

    public bool Enabled { get; init; } = false;
    public int Port { get; init; } = 6969;
    public int ReceiveBufferSize { get; init; } = 65536;
    public int MaxDatagramSize { get; init; } = 1500;
    public int ConnectionIdSweepIntervalSeconds { get; init; } = 30;
}
