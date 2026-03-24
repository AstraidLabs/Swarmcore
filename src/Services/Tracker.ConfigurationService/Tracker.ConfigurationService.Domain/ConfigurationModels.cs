using BeeTracker.BuildingBlocks.Domain.Primitives;

namespace Tracker.ConfigurationService.Domain;

public sealed class Torrent(Guid id, string infoHash, bool isPrivate, bool isEnabled) : Entity<Guid>(id)
{
    public string InfoHash { get; } = infoHash;
    public bool IsPrivate { get; } = isPrivate;
    public bool IsEnabled { get; } = isEnabled;
}

public sealed class PasskeyCredential(Guid id, Guid userId, string secretHash, bool isRevoked) : Entity<Guid>(id)
{
    public Guid UserId { get; } = userId;
    public string SecretHash { get; } = secretHash;
    public bool IsRevoked { get; } = isRevoked;
}
