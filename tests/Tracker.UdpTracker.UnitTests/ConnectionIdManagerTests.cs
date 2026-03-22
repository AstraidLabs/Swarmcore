using Tracker.UdpTracker.Protocol;

namespace Tracker.UdpTracker.UnitTests;

public class ConnectionIdManagerTests
{
    [Fact]
    public void Issue_ReturnsUniqueIds()
    {
        var manager = new ConnectionIdManager(ttlSeconds: 120);

        var id1 = manager.Issue();
        var id2 = manager.Issue();

        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void Validate_ValidId_ReturnsTrue()
    {
        var manager = new ConnectionIdManager(ttlSeconds: 120);
        var id = manager.Issue();

        Assert.True(manager.Validate(id));
    }

    [Fact]
    public void Validate_UnknownId_ReturnsFalse()
    {
        var manager = new ConnectionIdManager(ttlSeconds: 120);

        Assert.False(manager.Validate(99999L));
    }

    [Fact]
    public void Validate_ExpiredId_ReturnsFalse()
    {
        var manager = new ConnectionIdManager(ttlSeconds: 0);
        var id = manager.Issue();

        Thread.Sleep(50);

        Assert.False(manager.Validate(id));
    }

    [Fact]
    public void Sweep_RemovesExpiredEntries()
    {
        var manager = new ConnectionIdManager(ttlSeconds: 0);
        manager.Issue();
        manager.Issue();

        Thread.Sleep(50);

        manager.Sweep();

        Assert.Equal(0, manager.ActiveCount);
    }

    [Fact]
    public void Sweep_KeepsActiveEntries()
    {
        var manager = new ConnectionIdManager(ttlSeconds: 300);
        manager.Issue();
        manager.Issue();

        manager.Sweep();

        Assert.Equal(2, manager.ActiveCount);
    }
}
