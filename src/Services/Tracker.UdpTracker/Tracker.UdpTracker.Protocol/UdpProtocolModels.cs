namespace Tracker.UdpTracker.Protocol;

public enum UdpAction : int
{
    Connect = 0,
    Announce = 1,
    Scrape = 2,
    Error = 3
}

public readonly record struct UdpConnectRequest(long ProtocolId, int TransactionId);

public readonly record struct UdpConnectResponse(int TransactionId, long ConnectionId);

public readonly record struct UdpAnnounceRequest(
    long ConnectionId,
    int TransactionId,
    byte[] InfoHash,
    byte[] PeerId,
    long Downloaded,
    long Left,
    long Uploaded,
    int Event,
    uint IpAddress,
    uint Key,
    int NumWant,
    ushort Port);

public readonly record struct UdpAnnounceResponse(
    int TransactionId,
    int Interval,
    int Leechers,
    int Seeders,
    byte[] PeerData);

public readonly record struct UdpScrapeRequest(
    long ConnectionId,
    int TransactionId,
    byte[][] InfoHashes);

public readonly record struct UdpScrapeEntry(int Seeders, int Completed, int Leechers);

public readonly record struct UdpScrapeResponse(
    int TransactionId,
    UdpScrapeEntry[] Entries);

public readonly record struct UdpErrorResponse(int TransactionId, string Message);

public static class UdpProtocolConstants
{
    public const long ProtocolMagic = 0x41727101980;
    public const int ConnectRequestSize = 16;
    public const int AnnounceRequestMinSize = 98;
    public const int ScrapeRequestMinSize = 16;
    public const int ConnectResponseSize = 16;
    public const int AnnounceResponseHeaderSize = 20;
    public const int ScrapeEntrySize = 12;
}
