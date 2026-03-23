using Microsoft.Extensions.Options;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using Tracker.Gateway.Application.Announce;

namespace Tracker.Gateway.Infrastructure;

/// <summary>
/// Builds tracker URLs from <see cref="TrackerPublicEndpointOptions.BaseUrl"/>.
/// Never derives the public base from the incoming HTTP request — the application
/// runs on plain HTTP internally; HTTPS is terminated by Nginx.
/// </summary>
public sealed class TrackerUrlBuilder(IOptions<TrackerPublicEndpointOptions> options) : ITrackerUrlBuilder
{
    // Computed once; BaseUrl is validated as an absolute URI at startup.
    private readonly string _base = options.Value.BaseUrl.TrimEnd('/');

    public string GetAnnounceUrl() => $"{_base}/announce";

    public string GetAnnounceUrl(string passkey)
    {
        if (string.IsNullOrWhiteSpace(passkey))
        {
            return GetAnnounceUrl();
        }

        return $"{_base}/announce/{passkey.TrimStart('/')}";
    }

    public string GetScrapeUrl() => $"{_base}/scrape";
}
