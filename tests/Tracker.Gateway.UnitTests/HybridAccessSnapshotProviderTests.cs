using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using BeeTracker.BuildingBlocks.Abstractions.Options;
using BeeTracker.Contracts.Configuration;
using Tracker.Gateway.Infrastructure;

namespace Tracker.Gateway.UnitTests;

public sealed class HybridAccessSnapshotProviderTests
{
    [Fact]
    public async Task Miss_QueuesRefresh_AndSubsequentHitReturnsCachedValue()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var queue = new AccessRefreshQueue();
        var provider = new HybridAccessSnapshotProvider(
            memoryCache,
            queue,
            Options.Create(new PolicyCacheOptions
            {
                L1Seconds = 30,
                L2Seconds = 120
            }));

        var miss = await provider.GetTorrentPolicyAsync("ABC", CancellationToken.None);
        Assert.Null(miss);

        Assert.True(queue.Reader.TryRead(out var refresh));
        Assert.Equal("ABC", refresh.Key);

        provider.SetTorrentPolicy(new TorrentPolicyDto("ABC", true, true, 1800, 900, 50, 80, true, 1));

        var hit = await provider.GetTorrentPolicyAsync("ABC", CancellationToken.None);
        Assert.NotNull(hit);
        Assert.True(hit!.IsPrivate);
    }
}
