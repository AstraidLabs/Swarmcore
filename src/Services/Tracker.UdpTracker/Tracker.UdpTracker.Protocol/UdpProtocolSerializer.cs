using System.Buffers.Binary;
using System.Text;

namespace Tracker.UdpTracker.Protocol;

public static class UdpProtocolSerializer
{
    public static int WriteConnectResponse(Span<byte> buffer, in UdpConnectResponse response)
    {
        if (buffer.Length < UdpProtocolConstants.ConnectResponseSize)
        {
            return 0;
        }

        BinaryPrimitives.WriteInt32BigEndian(buffer[..4], (int)UdpAction.Connect);
        BinaryPrimitives.WriteInt32BigEndian(buffer[4..8], response.TransactionId);
        BinaryPrimitives.WriteInt64BigEndian(buffer[8..16], response.ConnectionId);
        return UdpProtocolConstants.ConnectResponseSize;
    }

    public static int WriteAnnounceResponse(Span<byte> buffer, in UdpAnnounceResponse response)
    {
        var totalSize = UdpProtocolConstants.AnnounceResponseHeaderSize + response.PeerData.Length;
        if (buffer.Length < totalSize)
        {
            return 0;
        }

        BinaryPrimitives.WriteInt32BigEndian(buffer[..4], (int)UdpAction.Announce);
        BinaryPrimitives.WriteInt32BigEndian(buffer[4..8], response.TransactionId);
        BinaryPrimitives.WriteInt32BigEndian(buffer[8..12], response.Interval);
        BinaryPrimitives.WriteInt32BigEndian(buffer[12..16], response.Leechers);
        BinaryPrimitives.WriteInt32BigEndian(buffer[16..20], response.Seeders);
        response.PeerData.AsSpan().CopyTo(buffer[20..]);
        return totalSize;
    }

    public static int WriteScrapeResponse(Span<byte> buffer, in UdpScrapeResponse response)
    {
        var totalSize = 8 + response.Entries.Length * UdpProtocolConstants.ScrapeEntrySize;
        if (buffer.Length < totalSize)
        {
            return 0;
        }

        BinaryPrimitives.WriteInt32BigEndian(buffer[..4], (int)UdpAction.Scrape);
        BinaryPrimitives.WriteInt32BigEndian(buffer[4..8], response.TransactionId);

        for (var i = 0; i < response.Entries.Length; i++)
        {
            var offset = 8 + i * UdpProtocolConstants.ScrapeEntrySize;
            BinaryPrimitives.WriteInt32BigEndian(buffer[offset..(offset + 4)], response.Entries[i].Seeders);
            BinaryPrimitives.WriteInt32BigEndian(buffer[(offset + 4)..(offset + 8)], response.Entries[i].Completed);
            BinaryPrimitives.WriteInt32BigEndian(buffer[(offset + 8)..(offset + 12)], response.Entries[i].Leechers);
        }

        return totalSize;
    }

    public static int WriteErrorResponse(Span<byte> buffer, in UdpErrorResponse response)
    {
        var messageBytes = Encoding.UTF8.GetByteCount(response.Message);
        var totalSize = 8 + messageBytes;
        if (buffer.Length < totalSize)
        {
            return 0;
        }

        BinaryPrimitives.WriteInt32BigEndian(buffer[..4], (int)UdpAction.Error);
        BinaryPrimitives.WriteInt32BigEndian(buffer[4..8], response.TransactionId);
        Encoding.UTF8.GetBytes(response.Message, buffer[8..]);
        return totalSize;
    }
}
