using Microsoft.Extensions.Options;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using Tracker.Gateway.Application.Announce;

namespace Tracker.Gateway.Infrastructure;

/// <summary>
/// Builds tracker URLs from <see cref="TrackerPublicEndpointOptions"/>.
/// Never derives the public base from the incoming HTTP request — the application
/// runs on plain HTTP internally; HTTPS is terminated by Nginx.
/// Per-surface URLs (AnnounceBaseUrl, ScrapeBaseUrl, etc.) override <see cref="TrackerPublicEndpointOptions.BaseUrl"/>
/// when configured, enabling separate subdomains for each surface.
/// </summary>
public sealed class TrackerUrlBuilder(IOptions<TrackerPublicEndpointOptions> options) : ITrackerUrlBuilder
{
    // Computed once; URLs are validated as absolute URIs at startup.
    private readonly string _announceBase = ResolveBase(options.Value.AnnounceBaseUrl, options.Value.BaseUrl);
    private readonly string _scrapeBase = ResolveBase(options.Value.ScrapeBaseUrl, options.Value.BaseUrl);
    private readonly string _adminBase = ResolveBase(options.Value.AdminBaseUrl, options.Value.BaseUrl);
    private readonly string _apiBase = ResolveBase(options.Value.ApiBaseUrl, options.Value.BaseUrl);

    public string GetAnnounceUrl() => $"{_announceBase}/announce";

    public string GetAnnounceUrl(string passkey)
    {
        if (string.IsNullOrWhiteSpace(passkey))
        {
            return GetAnnounceUrl();
        }

        return $"{_announceBase}/announce/{passkey.TrimStart('/')}";
    }

    public string GetScrapeUrl() => $"{_scrapeBase}/scrape";

    public string GetAdminBaseUrl() => _adminBase;

    public string GetApiBaseUrl() => _apiBase;

    private static string ResolveBase(string surfaceUrl, string fallbackUrl)
    {
        var url = string.IsNullOrWhiteSpace(surfaceUrl) ? fallbackUrl : surfaceUrl;
        return url.TrimEnd('/');
    }
}
