using Identity.SelfService.Infrastructure;

namespace Identity.SelfService.UnitTests;

public sealed class Sha256TokenHasherTests
{
    private readonly Sha256TokenHasher _hasher = new();

    [Fact]
    public void Hash_SameInput_ProducesSameOutput()
    {
        var hash1 = _hasher.Hash("test-token");
        var hash2 = _hasher.Hash("test-token");

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void Hash_DifferentInputs_ProduceDifferentOutputs()
    {
        var hash1 = _hasher.Hash("token-a");
        var hash2 = _hasher.Hash("token-b");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Hash_ReturnsBase64String()
    {
        var hash = _hasher.Hash("test-token");

        // SHA256 → 32 bytes → Base64 → 44 characters (with padding)
        Assert.Equal(44, hash.Length);
        Assert.EndsWith("=", hash);
    }

    [Fact]
    public void Hash_IsNotReversibleToPlainText()
    {
        var hash = _hasher.Hash("my-secret-token");
        Assert.DoesNotContain("my-secret-token", hash);
    }

    [Fact]
    public void GenerateRawToken_ReturnsNonEmpty()
    {
        var token = _hasher.GenerateRawToken();
        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Fact]
    public void GenerateRawToken_ReturnsUrlSafeString()
    {
        var token = _hasher.GenerateRawToken();

        Assert.DoesNotContain("+", token);
        Assert.DoesNotContain("/", token);
        Assert.DoesNotContain("=", token);
    }

    [Fact]
    public void GenerateRawToken_ProducesUniqueTokens()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => _hasher.GenerateRawToken()).ToHashSet();
        Assert.Equal(100, tokens.Count);
    }

    [Fact]
    public void GenerateRawToken_HasSufficientLength()
    {
        var token = _hasher.GenerateRawToken();
        // 32 bytes → Base64 without padding ≈ 43 chars
        Assert.True(token.Length >= 40, $"Token length {token.Length} is too short for 32 bytes of randomness");
    }

    [Fact]
    public void Hash_GeneratedToken_ProducesValidHash()
    {
        var raw = _hasher.GenerateRawToken();
        var hash = _hasher.Hash(raw);

        Assert.False(string.IsNullOrWhiteSpace(hash));
        Assert.NotEqual(raw, hash);
    }
}
