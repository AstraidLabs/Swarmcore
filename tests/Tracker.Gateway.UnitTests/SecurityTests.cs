using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using Swarmcore.BuildingBlocks.Abstractions.Time;
using Swarmcore.Contracts.Configuration;
using Tracker.Gateway.Infrastructure;

namespace Tracker.Gateway.UnitTests;

public sealed class PasskeyLifecycleGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 23, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
    }

    private sealed class FakeAccessSnapshotProvider : Tracker.Gateway.Application.Announce.IAccessSnapshotProvider
    {
        public Dictionary<string, PasskeyAccessDto> Passkeys { get; } = new();
        public Dictionary<string, TorrentPolicyDto> Policies { get; } = new();
        public Dictionary<Guid, UserPermissionSnapshotDto> Permissions { get; } = new();
        public Dictionary<string, BanRuleDto> Bans { get; } = new();

        public ValueTask<PasskeyAccessDto?> GetPasskeyAsync(string passkey, CancellationToken cancellationToken)
            => ValueTask.FromResult(Passkeys.GetValueOrDefault(passkey));

        public ValueTask<TorrentPolicyDto?> GetTorrentPolicyAsync(string infoHashHex, CancellationToken cancellationToken)
            => ValueTask.FromResult(Policies.GetValueOrDefault(infoHashHex));

        public ValueTask<UserPermissionSnapshotDto?> GetUserPermissionAsync(Guid userId, CancellationToken cancellationToken)
            => ValueTask.FromResult(Permissions.GetValueOrDefault(userId));

        public ValueTask<BanRuleDto?> GetBanRuleAsync(string scope, string subject, CancellationToken cancellationToken)
            => ValueTask.FromResult(Bans.GetValueOrDefault($"{scope}:{subject}"));
    }

    private static PasskeyLifecycleGuard CreateGuard(
        FakeAccessSnapshotProvider provider,
        FakeClock clock,
        SecurityHardeningOptions? options = null)
    {
        return new PasskeyLifecycleGuard(
            provider,
            clock,
            Options.Create(options ?? new SecurityHardeningOptions()),
            NullLogger<PasskeyLifecycleGuard>.Instance);
    }

    [Fact]
    public async Task RevokedPasskey_IsDenied()
    {
        var provider = new FakeAccessSnapshotProvider();
        var clock = new FakeClock();
        provider.Passkeys["revoked-key"] = new PasskeyAccessDto("revoked-key", Guid.NewGuid(), true, null, 1);

        var guard = CreateGuard(provider, clock);
        var result = await guard.ValidatePasskeyAsync("revoked-key", CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("passkey revoked", result.DenialReason);
    }

    [Fact]
    public async Task ExpiredPasskey_IsDenied()
    {
        var provider = new FakeAccessSnapshotProvider();
        var clock = new FakeClock();
        var expiredAt = Now.AddHours(-1);
        provider.Passkeys["expired-key"] = new PasskeyAccessDto("expired-key", Guid.NewGuid(), false, expiredAt, 1);

        var guard = CreateGuard(provider, clock);
        var result = await guard.ValidatePasskeyAsync("expired-key", CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("passkey expired", result.DenialReason);
    }

    [Fact]
    public async Task ValidPasskey_IsAllowed()
    {
        var provider = new FakeAccessSnapshotProvider();
        var clock = new FakeClock();
        var futureExpiry = Now.AddDays(30);
        provider.Passkeys["valid-key"] = new PasskeyAccessDto("valid-key", Guid.NewGuid(), false, futureExpiry, 1);

        var guard = CreateGuard(provider, clock);
        var result = await guard.ValidatePasskeyAsync("valid-key", CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Access);
        Assert.Null(result.DenialReason);
    }

    [Fact]
    public async Task UnknownPasskey_IsDenied()
    {
        var provider = new FakeAccessSnapshotProvider();
        var clock = new FakeClock();

        var guard = CreateGuard(provider, clock);
        var result = await guard.ValidatePasskeyAsync("unknown-key", CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("unknown passkey", result.DenialReason);
    }

    [Fact]
    public async Task EmptyPasskey_IsDenied()
    {
        var provider = new FakeAccessSnapshotProvider();
        var clock = new FakeClock();

        var guard = CreateGuard(provider, clock);
        var result = await guard.ValidatePasskeyAsync("", CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("passkey required", result.DenialReason);
    }

    [Fact]
    public async Task NullPasskey_IsDenied()
    {
        var provider = new FakeAccessSnapshotProvider();
        var clock = new FakeClock();

        var guard = CreateGuard(provider, clock);
        var result = await guard.ValidatePasskeyAsync(null!, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("passkey required", result.DenialReason);
    }

    [Fact]
    public async Task RotatedPasskey_OldKeyIsDenied_NewKeyIsAllowed()
    {
        var provider = new FakeAccessSnapshotProvider();
        var clock = new FakeClock();
        var userId = Guid.NewGuid();

        // Old key is revoked after rotation
        provider.Passkeys["old-key"] = new PasskeyAccessDto("old-key", userId, true, null, 2);
        // New key is active
        provider.Passkeys["new-key"] = new PasskeyAccessDto("new-key", userId, false, Now.AddDays(90), 1);

        var guard = CreateGuard(provider, clock);

        var oldResult = await guard.ValidatePasskeyAsync("old-key", CancellationToken.None);
        Assert.False(oldResult.IsValid);
        Assert.Equal("passkey revoked", oldResult.DenialReason);

        var newResult = await guard.ValidatePasskeyAsync("new-key", CancellationToken.None);
        Assert.True(newResult.IsValid);
    }

    [Fact]
    public async Task PasskeyWithNoExpiry_IsAllowed()
    {
        var provider = new FakeAccessSnapshotProvider();
        var clock = new FakeClock();
        provider.Passkeys["noexpiry"] = new PasskeyAccessDto("noexpiry", Guid.NewGuid(), false, null, 1);

        var guard = CreateGuard(provider, clock);
        var result = await guard.ValidatePasskeyAsync("noexpiry", CancellationToken.None);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task RevokedPasskey_AllowedWhenEnforcementDisabled()
    {
        var provider = new FakeAccessSnapshotProvider();
        var clock = new FakeClock();
        provider.Passkeys["revoked-key"] = new PasskeyAccessDto("revoked-key", Guid.NewGuid(), true, null, 1);

        var options = new SecurityHardeningOptions { RejectRevokedPasskeys = false };
        var guard = CreateGuard(provider, clock, options);
        var result = await guard.ValidatePasskeyAsync("revoked-key", CancellationToken.None);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ExpiredPasskey_AllowedWhenEnforcementDisabled()
    {
        var provider = new FakeAccessSnapshotProvider();
        var clock = new FakeClock();
        provider.Passkeys["expired-key"] = new PasskeyAccessDto("expired-key", Guid.NewGuid(), false, Now.AddHours(-1), 1);

        var options = new SecurityHardeningOptions { RejectExpiredPasskeys = false };
        var guard = CreateGuard(provider, clock, options);
        var result = await guard.ValidatePasskeyAsync("expired-key", CancellationToken.None);

        Assert.True(result.IsValid);
    }
}

public sealed class StartupSecurityValidatorTests
{
    [Fact]
    public void EmptyPostgresConnectionString_ThrowsOnValidation()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Options.Create(new SecurityHardeningOptions { RequireExplicitConnectionStrings = true }));
        services.AddSingleton(Options.Create(new PostgresOptions { ConnectionString = "" }));
        services.AddSingleton(Options.Create(new RedisOptions { Configuration = "localhost:6379" }));
        services.AddSingleton(Options.Create(new TrackerSecurityOptions()));
        var sp = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Swarmcore.Hosting.StartupSecurityValidator.Validate(sp, "test"));
        Assert.Contains("PostgreSQL connection string is empty", ex.Message);
    }

    [Fact]
    public void WeakPostgresPassword_ThrowsOnValidation()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Options.Create(new SecurityHardeningOptions { RequireExplicitConnectionStrings = true }));
        services.AddSingleton(Options.Create(new PostgresOptions { ConnectionString = "Host=localhost;Password=postgres" }));
        services.AddSingleton(Options.Create(new RedisOptions { Configuration = "localhost:6379" }));
        services.AddSingleton(Options.Create(new TrackerSecurityOptions()));
        var sp = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Swarmcore.Hosting.StartupSecurityValidator.Validate(sp, "test"));
        Assert.Contains("default/weak password", ex.Message);
    }

    [Fact]
    public void PolicyConflict_PasskeyInQueryStringWithEnforcement_ThrowsOnValidation()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Options.Create(new SecurityHardeningOptions
        {
            RequireExplicitConnectionStrings = false,
            EnforcePrivateTrackerPasskeyConsistency = true,
            RejectRevokedPasskeys = true
        }));
        services.AddSingleton(Options.Create(new TrackerSecurityOptions { AllowPasskeyInQueryString = true }));
        var sp = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Swarmcore.Hosting.StartupSecurityValidator.Validate(sp, "test"));
        Assert.Contains("AllowPasskeyInQueryString", ex.Message);
    }

    [Fact]
    public void ValidConfig_PassesValidation()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Options.Create(new SecurityHardeningOptions
        {
            RequireExplicitConnectionStrings = true,
            EnforcePrivateTrackerPasskeyConsistency = true
        }));
        services.AddSingleton(Options.Create(new PostgresOptions { ConnectionString = "Host=localhost;Password=StrongP@ss123!" }));
        services.AddSingleton(Options.Create(new RedisOptions { Configuration = "localhost:6379" }));
        services.AddSingleton(Options.Create(new TrackerSecurityOptions { AllowPasskeyInQueryString = false }));
        var sp = services.BuildServiceProvider();

        // Should not throw
        Swarmcore.Hosting.StartupSecurityValidator.Validate(sp, "test");
    }

    [Fact]
    public void DisabledValidation_SkipsChecks()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Options.Create(new SecurityHardeningOptions
        {
            RequireExplicitConnectionStrings = false,
            EnforcePrivateTrackerPasskeyConsistency = false
        }));
        services.AddSingleton(Options.Create(new PostgresOptions { ConnectionString = "" }));
        services.AddSingleton(Options.Create(new RedisOptions { Configuration = "" }));
        services.AddSingleton(Options.Create(new TrackerSecurityOptions { AllowPasskeyInQueryString = true }));
        var sp = services.BuildServiceProvider();

        // Should not throw when enforcement is disabled
        Swarmcore.Hosting.StartupSecurityValidator.Validate(sp, "test");
    }
}
