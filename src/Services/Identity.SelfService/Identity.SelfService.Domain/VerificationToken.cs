using Swarmcore.BuildingBlocks.Domain.Primitives;

namespace Identity.SelfService.Domain;

// ─── Token Purpose ──────────────────────────────────────────────────────────

public enum VerificationTokenPurpose
{
    AccountActivation = 0,
    AccountReactivation = 1,
    PasswordReset = 2,
    EmailConfirmation = 3,
}

// ─── Verification Token Entity ──────────────────────────────────────────────

public sealed class VerificationToken : Entity<Guid>
{
    private VerificationToken(Guid id) : base(id) { }

    public string UserId { get; private set; } = string.Empty;
    public VerificationTokenPurpose Purpose { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public bool IsConsumed => ConsumedAtUtc.HasValue;
    public bool IsRevoked => RevokedAtUtc.HasValue;

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAtUtc;

    public bool IsValid(DateTimeOffset now) => !IsConsumed && !IsRevoked && !IsExpired(now);

    public void Consume(DateTimeOffset now)
    {
        if (IsConsumed)
            throw new InvalidOperationException("Token has already been consumed.");

        if (IsRevoked)
            throw new InvalidOperationException("Token has been revoked.");

        if (IsExpired(now))
            throw new InvalidOperationException("Token has expired.");

        ConsumedAtUtc = now;
    }

    public void Revoke(DateTimeOffset now)
    {
        RevokedAtUtc = now;
    }

    public static VerificationToken Create(
        string userId,
        VerificationTokenPurpose purpose,
        string tokenHash,
        DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        if (expiresAt <= DateTimeOffset.UtcNow)
            throw new ArgumentException("Expiration must be in the future.", nameof(expiresAt));

        return new VerificationToken(Guid.NewGuid())
        {
            UserId = userId,
            Purpose = purpose,
            TokenHash = tokenHash,
            ExpiresAtUtc = expiresAt,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    public static VerificationToken Reconstitute(
        Guid id,
        string userId,
        VerificationTokenPurpose purpose,
        string tokenHash,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? consumedAtUtc,
        DateTimeOffset? revokedAtUtc)
    {
        return new VerificationToken(id)
        {
            UserId = userId,
            Purpose = purpose,
            TokenHash = tokenHash,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = createdAtUtc,
            ConsumedAtUtc = consumedAtUtc,
            RevokedAtUtc = revokedAtUtc,
        };
    }
}
