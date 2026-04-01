using Tracker.Gateway.Application.Announce;
using Tracker.Gateway.Infrastructure;

namespace Tracker.Gateway.UnitTests;

public sealed class AbuseEventChannelWriterTests
{
    private static AbuseEvent CreateEvent(string ip = "10.0.0.1") => new(
        Guid.NewGuid(), "test-node", ip, null,
        AbuseEventTypes.MalformedRequest, 5, null, DateTimeOffset.UtcNow);

    [Fact]
    public void TryWrite_SingleEvent_ReturnsTrue()
    {
        var writer = new AbuseEventChannelWriter();
        Assert.True(writer.TryWrite(CreateEvent()));
    }

    [Fact]
    public void TryWrite_EventAppearsOnReader()
    {
        var writer = new AbuseEventChannelWriter();
        var evt = CreateEvent("10.0.0.2");
        writer.TryWrite(evt);

        Assert.True(writer.Reader.TryRead(out var read));
        Assert.Equal(evt.Id, read.Id);
        Assert.Equal("10.0.0.2", read.Ip);
    }

    [Fact]
    public void TryWrite_MultipleEvents_AllReadable()
    {
        var writer = new AbuseEventChannelWriter();
        for (var i = 0; i < 100; i++)
        {
            writer.TryWrite(CreateEvent($"10.0.{i / 256}.{i % 256}"));
        }

        var count = 0;
        while (writer.Reader.TryRead(out _))
        {
            count++;
        }

        Assert.Equal(100, count);
    }

    [Fact]
    public void TryWrite_BoundedCapacity_DropsOldestWhenFull()
    {
        var writer = new AbuseEventChannelWriter();

        // Write more than the 8192 capacity
        for (var i = 0; i < 9_000; i++)
        {
            writer.TryWrite(CreateEvent());
        }

        // Should still be able to write (DropOldest mode)
        Assert.True(writer.TryWrite(CreateEvent()));
    }
}
