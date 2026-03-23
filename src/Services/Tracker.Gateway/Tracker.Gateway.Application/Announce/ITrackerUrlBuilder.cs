namespace Tracker.Gateway.Application.Announce;

/// <summary>
/// Builds publicly-advertised tracker URLs from the configured base URL.
/// Decouples URL construction from the incoming HTTP request so the application
/// can run behind a reverse proxy without leaking internal scheme/host.
/// </summary>
public interface ITrackerUrlBuilder
{
    /// <summary>Returns the base announce URL, e.g. https://announce.example.com/announce</summary>
    string GetAnnounceUrl();

    /// <summary>Returns the passkey-scoped announce URL, e.g. https://announce.example.com/announce/&lt;passkey&gt;</summary>
    string GetAnnounceUrl(string passkey);

    /// <summary>Returns the base scrape URL, e.g. https://tracker.example.com/scrape</summary>
    string GetScrapeUrl();

    /// <summary>Returns the admin UI base URL, e.g. https://admin.example.com</summary>
    string GetAdminBaseUrl();

    /// <summary>Returns the API base URL, e.g. https://api.example.com</summary>
    string GetApiBaseUrl();
}
