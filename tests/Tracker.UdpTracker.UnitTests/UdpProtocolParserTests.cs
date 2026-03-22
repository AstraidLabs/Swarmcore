using System.Buffers.Binary;
using Tracker.Gateway.Application.Announce;
using Tracker.UdpTracker.Protocol;

namespace Tracker.UdpTracker.UnitTests;

public class UdpProtocolParserTests
{
    [Fact]
    public void TryParseConnectRequest_ValidDatagram_ReturnsTrue()
    {
        var data = new byte[16];
        BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(0, 8), UdpProtocolConstants.ProtocolMagic);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8, 4), (int)UdpAction.Connect);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12, 4), 42);

        var result = UdpProtocolParser.TryParseConnectRequest(data, out var request);

        Assert.True(result);
        Assert.Equal(42, request.TransactionId);
        Assert.Equal(UdpProtocolConstants.ProtocolMagic, request.ProtocolId);
    }

    [Fact]
    public void TryParseConnectRequest_WrongMagic_ReturnsFalse()
    {
        var data = new byte[16];
        BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(0, 8), 0xDEADBEEF);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8, 4), (int)UdpAction.Connect);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12, 4), 1);

        Assert.False(UdpProtocolParser.TryParseConnectRequest(data, out _));
    }

    [Fact]
    public void TryParseConnectRequest_TooShort_ReturnsFalse()
    {
        var data = new byte[12];
        Assert.False(UdpProtocolParser.TryParseConnectRequest(data, out _));
    }

    [Fact]
    public void TryParseAnnounceRequest_ValidDatagram_ParsesAllFields()
    {
        var data = new byte[98];
        BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(0, 8), 12345L);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8, 4), (int)UdpAction.Announce);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12, 4), 99);
        // info_hash 16..36
        for (var i = 0; i < 20; i++) data[16 + i] = (byte)(i + 1);
        // peer_id 36..56
        for (var i = 0; i < 20; i++) data[36 + i] = (byte)(i + 0x41);
        BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(56, 8), 100L); // downloaded
        BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(64, 8), 500L); // left
        BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(72, 8), 200L); // uploaded
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(80, 4), 2); // event = started
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(84, 4), 0); // ip
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(88, 4), 0); // key
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(92, 4), 50); // numwant
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(96, 2), 6881); // port

        var result = UdpProtocolParser.TryParseAnnounceRequest(data, out var request);

        Assert.True(result);
        Assert.Equal(12345L, request.ConnectionId);
        Assert.Equal(99, request.TransactionId);
        Assert.Equal(100L, request.Downloaded);
        Assert.Equal(500L, request.Left);
        Assert.Equal(200L, request.Uploaded);
        Assert.Equal(2, request.Event);
        Assert.Equal(50, request.NumWant);
        Assert.Equal((ushort)6881, request.Port);
    }

    [Fact]
    public void TryParseScrapeRequest_ValidDatagram_ParsesInfoHashes()
    {
        var data = new byte[16 + 40]; // header + 2 info hashes
        BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(0, 8), 12345L);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8, 4), (int)UdpAction.Scrape);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12, 4), 7);
        for (var i = 0; i < 20; i++) data[16 + i] = 0xAA;
        for (var i = 0; i < 20; i++) data[36 + i] = 0xBB;

        var result = UdpProtocolParser.TryParseScrapeRequest(data, out var request);

        Assert.True(result);
        Assert.Equal(2, request.InfoHashes.Length);
        Assert.Equal(7, request.TransactionId);
    }

    [Fact]
    public void TryParseScrapeRequest_NoInfoHashes_ReturnsFalse()
    {
        var data = new byte[16];
        BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(0, 8), 12345L);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8, 4), (int)UdpAction.Scrape);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12, 4), 1);

        Assert.False(UdpProtocolParser.TryParseScrapeRequest(data, out _));
    }

    [Theory]
    [InlineData(0, TrackerEvent.None)]
    [InlineData(1, TrackerEvent.Completed)]
    [InlineData(2, TrackerEvent.Started)]
    [InlineData(3, TrackerEvent.Stopped)]
    [InlineData(99, TrackerEvent.None)]
    public void MapUdpEvent_MapsCorrectly(int udpEvent, TrackerEvent expected)
    {
        Assert.Equal(expected, UdpProtocolParser.MapUdpEvent(udpEvent));
    }
}
