using Identity.SelfService.Domain;

namespace Identity.SelfService.UnitTests;

public sealed class VerificationTokenTests
{
    private static readonly DateTimeOffset FutureExpiry = DateTimeOffset.UtcNow.AddHours(24);

    // ─── Create ────────────────────────────────────────────────────────────

    [Fact]
    public void Create_ValidInputs_SetsProperties()
    {
        var token = VerificationToken.Create("user-1", VerificationTokenPurpose.AccountActivation, "hash123", FutureExpiry);

        Assert.Equal("user-1", token.UserId);
        Assert.Equal(VerificationTokenPurpose.AccountActivation, token.Purpose);
        Assert.Equal("hash123", token.TokenHash);
        Assert.Equal(FutureExpiry, token.ExpiresAtUtc);
        Assert.NotEqual(Guid.Empty, token.Id);
        Assert.False(token.IsConsumed);
        Assert.False(token.IsRevoked);
    }

    [Fact]
    public void Create_NullUserId_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            VerificationToken.Create(null!, VerificationTokenPurpose.AccountActivation, "hash", FutureExpiry));
    }

    [Fact]
    public void Create_EmptyUserId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            VerificationToken.Create("", VerificationTokenPurpose.AccountActivation, "hash", FutureExpiry));
    }

    [Fact]
    public void Create_WhitespaceUserId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            VerificationToken.Create("   ", VerificationTokenPurpose.AccountActivation, "hash", FutureExpiry));
    }

    [Fact]
    public void Create_NullTokenHash_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            VerificationToken.Create("user-1", VerificationTokenPurpose.AccountActivation, null!, FutureExpiry));
    }

    [Fact]
    public void Create_PastExpiry_Throws()
    {
        var past = DateTimeOffset.UtcNow.AddHours(-1);
        Assert.Throws<ArgumentException>(() =>
            VerificationToken.Create("user-1", VerificationTokenPurpose.AccountActivation, "hash", past));
    }

    // ─── Validity ──────────────────────────────────────────────────────────

    [Fact]
    public void IsValid_FreshToken_ReturnsTrue()
    {
        var token = VerificationToken.Create("user-1", VerificationTokenPurpose.PasswordReset, "hash", FutureExpiry);
        var now = DateTimeOffset.UtcNow;

        Assert.True(token.IsValid(now));
    }

    [Fact]
    public void IsExpired_BeforeExpiry_ReturnsFalse()
    {
        var token = VerificationToken.Create("user-1", VerificationTokenPurpose.PasswordReset, "hash", FutureExpiry);

        Assert.False(token.IsExpired(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsExpired_AtExpiry_ReturnsTrue()
    {
        var token = VerificationToken.Create("user-1", VerificationTokenPurpose.PasswordReset, "hash", FutureExpiry);

        Assert.True(token.IsExpired(FutureExpiry));
    }

    [Fact]
    public void IsExpired_AfterExpiry_ReturnsTrue()
    {
        var token = VerificationToken.Create("user-1", VerificationTokenPurpose.PasswordReset, "hash", FutureExpiry);

        Assert.True(token.IsExpired(FutureExpiry.AddSeconds(1)));
    }

    // ─── Consume ───────────────────────────────────────────────────────────

    [Fact]
    public void Consume_FreshToken_SetsConsumedAtUtc()
    {
        var token = VerificationToken.Create("user-1", VerificationTokenPurpose.AccountActivation, "hash", FutureExpiry);
        var now = DateTimeOffset.UtcNow;

        token.Consume(now);

        Assert.True(token.IsConsumed);
        Assert.Equal(now, token.ConsumedAtUtc);
    }

    [Fact]
    public void Consume_AlreadyConsumed_Throws()
    {
        var token = VerificationToken.Create("user-1", VerificationTokenPurpose.AccountActivation, "hash", FutureExpiry);
        token.Consume(DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => token.Consume(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Consume_RevokedToken_Throws()
    {
        var token = VerificationToken.Create("user-1", VerificationTokenPurpose.AccountActivation, "hash", FutureExpiry);
        token.Revoke(DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => token.Consume(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Consume_ExpiredToken_Throws()
    {
        var token = VerificationToken.Create("user-1", VerificationTokenPurpose.AccountActivation, "hash", FutureExpiry);

        Assert.Throws<InvalidOperationException>(() => token.Consume(FutureExpiry.AddMinutes(1)));
    }

    [Fact]
    public void IsValid_ConsumedToken_ReturnsFalse()
    {
        var token = VerificationToken.Create("user-1", VerificationTokenPurpose.AccountActivation, "hash", FutureExpiry);
        token.Consume(DateTimeOffset.UtcNow);

        Assert.False(token.IsValid(DateTimeOffset.UtcNow));
    }

    // ─── Revoke ────────────────────────────────────────────────────────────

    [Fact]
    public void Revoke_SetsRevokedAtUtc()
    {
        var token = VerificationToken.Create("user-1", VerificationTokenPurpose.AccountReactivation, "hash", FutureExpiry);
        var now = DateTimeOffset.UtcNow;

        token.Revoke(now);

        Assert.True(token.IsRevoked);
        Assert.Equal(now, token.RevokedAtUtc);
    }

    [Fact]
    public void IsValid_RevokedToken_ReturnsFalse()
    {
        var token = VerificationToken.Create("user-1", VerificationTokenPurpose.AccountReactivation, "hash", FutureExpiry);
        token.Revoke(DateTimeOffset.UtcNow);

        Assert.False(token.IsValid(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Revoke_CanRevokeMultipleTimes()
    {
        var token = VerificationToken.Create("user-1", VerificationTokenPurpose.AccountReactivation, "hash", FutureExpiry);
        var first = DateTimeOffset.UtcNow;
        var second = first.AddMinutes(5);

        token.Revoke(first);
        token.Revoke(second);

        Assert.Equal(second, token.RevokedAtUtc);
    }

    // ─── Reconstitute ──────────────────────────────────────────────────────

    [Fact]
    public void Reconstitute_RestoresFullState()
    {
        var id = Guid.NewGuid();
        var created = DateTimeOffset.UtcNow.AddDays(-1);
        var expires = DateTimeOffset.UtcNow.AddDays(1);
        var consumed = DateTimeOffset.UtcNow;

        var token = VerificationToken.Reconstitute(
            id, "user-42", VerificationTokenPurpose.EmailConfirmation,
            "stored-hash", expires, created, consumed, null);

        Assert.Equal(id, token.Id);
        Assert.Equal("user-42", token.UserId);
        Assert.Equal(VerificationTokenPurpose.EmailConfirmation, token.Purpose);
        Assert.Equal("stored-hash", token.TokenHash);
        Assert.Equal(expires, token.ExpiresAtUtc);
        Assert.Equal(created, token.CreatedAtUtc);
        Assert.Equal(consumed, token.ConsumedAtUtc);
        Assert.Null(token.RevokedAtUtc);
        Assert.True(token.IsConsumed);
        Assert.False(token.IsRevoked);
    }

    [Fact]
    public void Reconstitute_WithRevokedState()
    {
        var revoked = DateTimeOffset.UtcNow;
        var token = VerificationToken.Reconstitute(
            Guid.NewGuid(), "user-1", VerificationTokenPurpose.PasswordReset,
            "hash", DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(-1), null, revoked);

        Assert.True(token.IsRevoked);
        Assert.False(token.IsConsumed);
        Assert.Equal(revoked, token.RevokedAtUtc);
    }

    // ─── Purpose enum ──────────────────────────────────────────────────────

    [Fact]
    public void VerificationTokenPurpose_HasExpectedValues()
    {
        Assert.Equal(0, (int)VerificationTokenPurpose.AccountActivation);
        Assert.Equal(1, (int)VerificationTokenPurpose.AccountReactivation);
        Assert.Equal(2, (int)VerificationTokenPurpose.PasswordReset);
        Assert.Equal(3, (int)VerificationTokenPurpose.EmailConfirmation);
    }

    [Theory]
    [InlineData(VerificationTokenPurpose.AccountActivation)]
    [InlineData(VerificationTokenPurpose.AccountReactivation)]
    [InlineData(VerificationTokenPurpose.PasswordReset)]
    [InlineData(VerificationTokenPurpose.EmailConfirmation)]
    public void Create_AllPurposes_Succeed(VerificationTokenPurpose purpose)
    {
        var token = VerificationToken.Create("user-1", purpose, "hash", FutureExpiry);
        Assert.Equal(purpose, token.Purpose);
    }
}
