using System.Buffers.Binary;
using Tracker.UdpTracker.Protocol;

namespace Tracker.UdpTracker.UnitTests;

public class UdpProtocolSerializerTests
{
    [Fact]
    public void WriteConnectResponse_ProducesCorrectBytes()
    {
        var buffer = new byte[16];
        var response = new UdpConnectResponse(42, 12345L);

        var written = UdpProtocolSerializer.WriteConnectResponse(buffer, response);

        Assert.Equal(16, written);
        Assert.Equal((int)UdpAction.Connect, BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(0, 4)));
        Assert.Equal(42, BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(4, 4)));
        Assert.Equal(12345L, BinaryPrimitives.ReadInt64BigEndian(buffer.AsSpan(8, 8)));
    }

    [Fact]
    public void WriteAnnounceResponse_ProducesCorrectLayout()
    {
        var peerData = new byte[12]; // 2 peers
        peerData[0] = 10; peerData[1] = 0; peerData[2] = 0; peerData[3] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(peerData.AsSpan(4, 2), 6881);
        peerData[6] = 192; peerData[7] = 168; peerData[8] = 1; peerData[9] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(peerData.AsSpan(10, 2), 6882);

        var response = new UdpAnnounceResponse(7, 1800, 5, 10, peerData);
        var buffer = new byte[32];

        var written = UdpProtocolSerializer.WriteAnnounceResponse(buffer, response);

        Assert.Equal(32, written);
        Assert.Equal((int)UdpAction.Announce, BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(0, 4)));
        Assert.Equal(7, BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(4, 4)));
        Assert.Equal(1800, BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(8, 4)));
        Assert.Equal(5, BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(12, 4)));
        Assert.Equal(10, BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(16, 4)));
        Assert.Equal(peerData, buffer[20..32]);
    }

    [Fact]
    public void WriteScrapeResponse_ProducesCorrectLayout()
    {
        var entries = new[]
        {
            new UdpScrapeEntry(10, 50, 5),
            new UdpScrapeEntry(20, 100, 3)
        };
        var response = new UdpScrapeResponse(8, entries);
        var buffer = new byte[8 + 24];

        var written = UdpProtocolSerializer.WriteScrapeResponse(buffer, response);

        Assert.Equal(32, written);
        Assert.Equal((int)UdpAction.Scrape, BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(0, 4)));
        Assert.Equal(8, BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(4, 4)));
        Assert.Equal(10, BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(8, 4)));
        Assert.Equal(50, BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(12, 4)));
        Assert.Equal(5, BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(16, 4)));
        Assert.Equal(20, BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(20, 4)));
        Assert.Equal(100, BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(24, 4)));
        Assert.Equal(3, BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(28, 4)));
    }

    [Fact]
    public void WriteErrorResponse_ProducesCorrectBytes()
    {
        var response = new UdpErrorResponse(5, "test error");
        var buffer = new byte[64];

        var written = UdpProtocolSerializer.WriteErrorResponse(buffer, response);

        Assert.Equal(8 + System.Text.Encoding.UTF8.GetByteCount("test error"), written);
        Assert.Equal((int)UdpAction.Error, BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(0, 4)));
        Assert.Equal(5, BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(4, 4)));
        Assert.Equal("test error", System.Text.Encoding.UTF8.GetString(buffer.AsSpan(8, written - 8)));
    }

    [Fact]
    public void WriteConnectResponse_BufferTooSmall_ReturnsZero()
    {
        var buffer = new byte[8];
        Assert.Equal(0, UdpProtocolSerializer.WriteConnectResponse(buffer, new UdpConnectResponse(1, 2)));
    }
}
