using BeeTracker.Contracts.Configuration;

namespace Tracker.Gateway.Application.Announce;

public readonly record struct ScrapeRequest(
    string? Passkey,
    InfoHashKey[] InfoHashes);

public readonly record struct ScrapeAccessResolution(bool IsAllowed, TorrentPolicyDto? Policy)
{
    public static ScrapeAccessResolution Allow(TorrentPolicyDto policy) => new(true, policy);
    public static ScrapeAccessResolution Deny() => new(false, null);
}

public readonly record struct ScrapeFileEntry(
    InfoHashKey InfoHash,
    int SeederCount,
    int LeecherCount,
    int DownloadedCount);

public readonly record struct ScrapeSuccess(
    ScrapeFileEntry[] Files);
