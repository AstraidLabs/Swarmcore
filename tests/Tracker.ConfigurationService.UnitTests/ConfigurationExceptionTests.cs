using Tracker.ConfigurationService.Application;

namespace Tracker.ConfigurationService.UnitTests;

public sealed class ConfigurationConcurrencyExceptionTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var ex = new ConfigurationConcurrencyException("Torrent", "AABB", 1, 2);

        Assert.Equal("Torrent", ex.EntityType);
        Assert.Equal("AABB", ex.EntityKey);
        Assert.Equal(1, ex.ExpectedVersion);
        Assert.Equal(2, ex.ActualVersion);
    }

    [Fact]
    public void Constructor_MessageContainsEntityTypeAndKey()
    {
        var ex = new ConfigurationConcurrencyException("TorrentPolicy", "CCDD", 3, 5);

        Assert.Contains("TorrentPolicy", ex.Message);
        Assert.Contains("CCDD", ex.Message);
    }

    [Fact]
    public void Constructor_MessageContainsVersionNumbers()
    {
        var ex = new ConfigurationConcurrencyException("Passkey", "pk-1", 10, 12);

        Assert.Contains("10", ex.Message);
        Assert.Contains("12", ex.Message);
    }

    [Fact]
    public void IsException()
    {
        var ex = new ConfigurationConcurrencyException("T", "K", 0, 1);
        Assert.IsAssignableFrom<Exception>(ex);
    }
}

public sealed class ConfigurationEntityNotFoundExceptionTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var ex = new ConfigurationEntityNotFoundException("Torrent", "AABBCCDD");

        Assert.Equal("Torrent", ex.EntityType);
        Assert.Equal("AABBCCDD", ex.EntityKey);
    }

    [Fact]
    public void Constructor_MessageContainsEntityTypeAndKey()
    {
        var ex = new ConfigurationEntityNotFoundException("BanRule", "ip:192.168.1.1");

        Assert.Contains("BanRule", ex.Message);
        Assert.Contains("ip:192.168.1.1", ex.Message);
    }

    [Fact]
    public void Constructor_MessageIndicatesNotFound()
    {
        var ex = new ConfigurationEntityNotFoundException("Passkey", "pk-abc");

        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public void IsException()
    {
        var ex = new ConfigurationEntityNotFoundException("T", "K");
        Assert.IsAssignableFrom<Exception>(ex);
    }
}
