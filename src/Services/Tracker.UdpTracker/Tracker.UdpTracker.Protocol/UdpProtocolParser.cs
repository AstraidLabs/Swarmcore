using System.Buffers.Binary;
using Tracker.Gateway.Application.Announce;

namespace Tracker.UdpTracker.Protocol;

public static class UdpProtocolParser
{
    public static bool TryParseAction(ReadOnlySpan<byte> data, out UdpAction action, out int transactionId)
    {
        action = default;
        transactionId = default;

        if (data.Length < 12)
        {
            return false;
        }

        action = (UdpAction)BinaryPrimitives.ReadInt32BigEndian(data[8..12]);
        if (data.Length >= 16)
        {
            transactionId = BinaryPrimitives.ReadInt32BigEndian(data[12..16]);
        }

        return true;
    }

    public static bool TryParseConnectRequest(ReadOnlySpan<byte> data, out UdpConnectRequest request)
    {
        request = default;

        if (data.Length < UdpProtocolConstants.ConnectRequestSize)
        {
            return false;
        }

        var protocolId = BinaryPrimitives.ReadInt64BigEndian(data[..8]);
        var action = BinaryPrimitives.ReadInt32BigEndian(data[8..12]);
        var transactionId = BinaryPrimitives.ReadInt32BigEndian(data[12..16]);

        if (protocolId != UdpProtocolConstants.ProtocolMagic || action != (int)UdpAction.Connect)
        {
            return false;
        }

        request = new UdpConnectRequest(protocolId, transactionId);
        return true;
    }

    public static bool TryParseAnnounceRequest(ReadOnlySpan<byte> data, out UdpAnnounceRequest request)
    {
        request = default;

        if (data.Length < UdpProtocolConstants.AnnounceRequestMinSize)
        {
            return false;
        }

        var connectionId = BinaryPrimitives.ReadInt64BigEndian(data[..8]);
        var transactionId = BinaryPrimitives.ReadInt32BigEndian(data[12..16]);
        var infoHash = data[16..36].ToArray();
        var peerId = data[36..56].ToArray();
        var downloaded = BinaryPrimitives.ReadInt64BigEndian(data[56..64]);
        var left = BinaryPrimitives.ReadInt64BigEndian(data[64..72]);
        var uploaded = BinaryPrimitives.ReadInt64BigEndian(data[72..80]);
        var udpEvent = BinaryPrimitives.ReadInt32BigEndian(data[80..84]);
        var ipAddress = BinaryPrimitives.ReadUInt32BigEndian(data[84..88]);
        var key = BinaryPrimitives.ReadUInt32BigEndian(data[88..92]);
        var numWant = BinaryPrimitives.ReadInt32BigEndian(data[92..96]);
        var port = BinaryPrimitives.ReadUInt16BigEndian(data[96..98]);

        request = new UdpAnnounceRequest(connectionId, transactionId, infoHash, peerId, downloaded, left, uploaded, udpEvent, ipAddress, key, numWant, port);
        return true;
    }

    public static bool TryParseScrapeRequest(ReadOnlySpan<byte> data, out UdpScrapeRequest request)
    {
        request = default;

        if (data.Length < UdpProtocolConstants.ScrapeRequestMinSize)
        {
            return false;
        }

        var connectionId = BinaryPrimitives.ReadInt64BigEndian(data[..8]);
        var transactionId = BinaryPrimitives.ReadInt32BigEndian(data[12..16]);

        var remaining = data[16..];
        if (remaining.Length % 20 != 0 || remaining.Length == 0)
        {
            return false;
        }

        var hashCount = remaining.Length / 20;
        var infoHashes = new byte[hashCount][];
        for (var i = 0; i < hashCount; i++)
        {
            infoHashes[i] = remaining.Slice(i * 20, 20).ToArray();
        }

        request = new UdpScrapeRequest(connectionId, transactionId, infoHashes);
        return true;
    }

    public static TrackerEvent MapUdpEvent(int udpEvent) => udpEvent switch
    {
        0 => TrackerEvent.None,
        1 => TrackerEvent.Completed,
        2 => TrackerEvent.Started,
        3 => TrackerEvent.Stopped,
        _ => TrackerEvent.None
    };
}
