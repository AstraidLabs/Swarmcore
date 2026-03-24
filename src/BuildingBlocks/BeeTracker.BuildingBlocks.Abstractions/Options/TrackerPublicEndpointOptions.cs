using System.ComponentModel.DataAnnotations;

namespace BeeTracker.BuildingBlocks.Abstractions.Options;

/// <summary>
/// Configuration for the publicly-advertised tracker endpoint.
/// Used to construct announce/scrape URLs returned to clients.
/// The application runs internally on plain HTTP; HTTPS is terminated by Nginx.
/// </summary>
public sealed class TrackerPublicEndpointOptions : IValidatableObject
{
    public const string SectionName = "BeeTracker:PublicEndpoint";

    /// <summary>
    /// Absolute base URL advertised to BitTorrent clients.
    /// Example: https://tracker.example.com
    /// </summary>
    [Required]
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// When true, validates that <see cref="BaseUrl"/> uses the https scheme.
    /// Set to false only for local development without a reverse proxy.
    /// </summary>
    public bool ForceHttps { get; init; } = true;

    /// <summary>
    /// Emit HSTS headers (Strict-Transport-Security).
    /// Useful when the application is exposed directly; leave false when Nginx handles HSTS.
    /// </summary>
    public bool EnableHsts { get; init; } = false;

    /// <summary>
    /// Max-age in seconds for the HSTS header. Ignored when <see cref="EnableHsts"/> is false.
    /// </summary>
    public int HstsMaxAgeSeconds { get; init; } = 31536000; // 1 year

    /// <summary>
    /// Redirect plain HTTP requests to HTTPS at the application layer.
    /// Keep false when the reverse proxy (Nginx) already handles the redirect.
    /// </summary>
    public bool EnableHttpsRedirection { get; init; } = false;

    // ── Subdomain and multi-host support ─────────────────────────────────────

    /// <summary>
    /// Root domain for subdomain resolution. Required when subdomain validation is active.
    /// Example: example.com
    /// </summary>
    public string BaseDomain { get; init; } = string.Empty;

    /// <summary>
    /// Optional per-surface base URLs. Falls back to <see cref="BaseUrl"/> when empty.
    /// </summary>
    public string AnnounceBaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// Public base URL for scrape surface. Falls back to <see cref="BaseUrl"/> when empty.
    /// </summary>
    public string ScrapeBaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// Public base URL for the admin UI surface.
    /// </summary>
    public string AdminBaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// Public base URL for the API surface.
    /// </summary>
    public string ApiBaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// Explicit list of allowed Host header values. Requests with a Host not in this list
    /// are rejected by <c>HostValidationMiddleware</c>. Empty list disables host validation.
    /// </summary>
    public string[] AllowedHosts { get; init; } = [];

    /// <summary>
    /// Allowed subdomain prefixes (e.g. "announce", "admin").
    /// Used together with <see cref="BaseDomain"/> for subdomain validation.
    /// </summary>
    public string[] AllowedSubdomains { get; init; } = [];

    /// <summary>
    /// When true, any subdomain of <see cref="BaseDomain"/> is accepted.
    /// When false, only subdomains listed in <see cref="AllowedSubdomains"/> are accepted.
    /// </summary>
    public bool EnableWildcardSubdomains { get; init; } = false;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            yield return new ValidationResult(
                "PublicEndpoint.BaseUrl must be configured.",
                [nameof(BaseUrl)]);
            yield break;
        }

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri))
        {
            yield return new ValidationResult(
                $"PublicEndpoint.BaseUrl '{BaseUrl}' is not a valid absolute URI.",
                [nameof(BaseUrl)]);
            yield break;
        }

        if (ForceHttps && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            yield return new ValidationResult(
                $"PublicEndpoint.BaseUrl must use the https scheme when ForceHttps is true. Got: '{uri.Scheme}'.",
                [nameof(BaseUrl), nameof(ForceHttps)]);
        }

        // Validate per-surface URLs when provided.
        foreach (var (url, name) in new[]
        {
            (AnnounceBaseUrl, nameof(AnnounceBaseUrl)),
            (ScrapeBaseUrl, nameof(ScrapeBaseUrl)),
            (AdminBaseUrl, nameof(AdminBaseUrl)),
            (ApiBaseUrl, nameof(ApiBaseUrl)),
        })
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var surfaceUri))
            {
                yield return new ValidationResult(
                    $"PublicEndpoint.{name} '{url}' is not a valid absolute URI.",
                    [name]);
                continue;
            }

            if (ForceHttps && !string.Equals(surfaceUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                yield return new ValidationResult(
                    $"PublicEndpoint.{name} must use the https scheme when ForceHttps is true. Got: '{surfaceUri.Scheme}'.",
                    [name, nameof(ForceHttps)]);
            }
        }

        // BaseDomain is required when subdomain lists are configured.
        if ((AllowedSubdomains.Length > 0 || EnableWildcardSubdomains) && string.IsNullOrWhiteSpace(BaseDomain))
        {
            yield return new ValidationResult(
                "PublicEndpoint.BaseDomain must be set when AllowedSubdomains or EnableWildcardSubdomains is active.",
                [nameof(BaseDomain)]);
        }
    }
}
