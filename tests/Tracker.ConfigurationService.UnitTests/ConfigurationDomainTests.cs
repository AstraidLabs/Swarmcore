using Tracker.ConfigurationService.Domain;

namespace Tracker.ConfigurationService.UnitTests;

public sealed class TorrentTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var id = Guid.NewGuid();
        var torrent = new Torrent(id, "AABBCCDD00112233445566778899AABBCCDDEEFF", isPrivate: true, isEnabled: true);

        Assert.Equal(id, torrent.Id);
        Assert.Equal("AABBCCDD00112233445566778899AABBCCDDEEFF", torrent.InfoHash);
        Assert.True(torrent.IsPrivate);
        Assert.True(torrent.IsEnabled);
    }

    [Fact]
    public void Constructor_PublicDisabledTorrent()
    {
        var torrent = new Torrent(Guid.NewGuid(), "0000000000000000000000000000000000000000", isPrivate: false, isEnabled: false);

        Assert.False(torrent.IsPrivate);
        Assert.False(torrent.IsEnabled);
    }

    [Fact]
    public void Constructor_InfoHashIsPreservedExactly()
    {
        var infoHash = "aabbccdd00112233445566778899aabbccddeeff";
        var torrent = new Torrent(Guid.NewGuid(), infoHash, false, true);

        Assert.Equal(infoHash, torrent.InfoHash);
    }

    [Fact]
    public void Id_IsSetFromConstructor()
    {
        var id = Guid.NewGuid();
        var torrent = new Torrent(id, "AAAA", false, false);

        Assert.Equal(id, torrent.Id);
    }
}

public sealed class PasskeyCredentialTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var credential = new PasskeyCredential(id, userId, "sha256-hash-value", isRevoked: false);

        Assert.Equal(id, credential.Id);
        Assert.Equal(userId, credential.UserId);
        Assert.Equal("sha256-hash-value", credential.SecretHash);
        Assert.False(credential.IsRevoked);
    }

    [Fact]
    public void Constructor_RevokedCredential()
    {
        var credential = new PasskeyCredential(Guid.NewGuid(), Guid.NewGuid(), "hash", isRevoked: true);

        Assert.True(credential.IsRevoked);
    }

    [Fact]
    public void Constructor_SecretHashIsPreserved()
    {
        var hash = "c3ab8ff13720e8ad9047dd39466b3c8974e592c2fa383d4a3960714caef0c4f2";
        var credential = new PasskeyCredential(Guid.NewGuid(), Guid.NewGuid(), hash, false);

        Assert.Equal(hash, credential.SecretHash);
    }
}
