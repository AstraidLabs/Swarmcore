using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using BeeTracker.BuildingBlocks.Abstractions.Options;
using BeeTracker.BuildingBlocks.Abstractions.Time;
using BeeTracker.Contracts.Configuration;
using Tracker.Gateway.Application.Announce;
using Tracker.Gateway.Infrastructure;

namespace Tracker.Gateway.UnitTests;

public sealed class IpBanGuardTests : IDisposable
{
    private readonly MemoryCache _memoryCache = new(new MemoryCacheOptions());
    private readonly AccessRefreshQueue _queue = new();
    private readonly TestClock _clock = new();

    private HybridAccessSnapshotProvider CreateProvider()
    {
        return new HybridAccessSnapshotProvider(
            _memoryCache,
            _queue,
            Options.Create(new PolicyCacheOptions { L1Seconds = 30, L2Seconds = 120 }));
    }

    [Fact]
    public async Task EvaluateAsync_NoBanRule_ReturnsNull()
    {
        var provider = CreateProvider();
        var guard = new IpBanGuard(provider, _clock);

        var result = await guard.EvaluateAsync("10.0.0.1", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_ActiveBanRule_ReturnsForbidden()
    {
        var provider = CreateProvider();
        provider.SetBanRule(new BanRuleDto(TrackerBanScopes.Ip, "10.0.0.2", "Abuse detected", null, 1));
        var guard = new IpBanGuard(provider, _clock);

        var result = await guard.EvaluateAsync("10.0.0.2", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.Value.StatusCode);
        Assert.Equal("Abuse detected", result.Value.FailureReason);
    }

    [Fact]
    public async Task EvaluateAsync_ExpiredBanRule_ReturnsNull()
    {
        _clock.UtcNow = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
        var provider = CreateProvider();
        provider.SetBanRule(new BanRuleDto(TrackerBanScopes.Ip, "10.0.0.3", "Old ban",
            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), 1));
        var guard = new IpBanGuard(provider, _clock);

        var result = await guard.EvaluateAsync("10.0.0.3", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_FutureBanRule_ReturnsForbidden()
    {
        _clock.UtcNow = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
        var provider = CreateProvider();
        provider.SetBanRule(new BanRuleDto(TrackerBanScopes.Ip, "10.0.0.4", "Temp ban",
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), 1));
        var guard = new IpBanGuard(provider, _clock);

        var result = await guard.EvaluateAsync("10.0.0.4", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.Value.StatusCode);
    }

    [Fact]
    public async Task EvaluateAsync_DifferentScope_DoesNotBlock()
    {
        var provider = CreateProvider();
        // Ban a passkey, not an IP
        provider.SetBanRule(new BanRuleDto(TrackerBanScopes.Passkey, "10.0.0.5", "Passkey ban", null, 1));
        var guard = new IpBanGuard(provider, _clock);

        var result = await guard.EvaluateAsync("10.0.0.5", CancellationToken.None);

        Assert.Null(result);
    }

    public void Dispose() => _memoryCache.Dispose();

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    }
}
