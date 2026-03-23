using System.ComponentModel.DataAnnotations;

namespace Swarmcore.BuildingBlocks.Abstractions.Options;

/// <summary>
/// Configuration for the publicly-advertised tracker endpoint.
/// Used to construct announce/scrape URLs returned to clients.
/// The application runs internally on plain HTTP; HTTPS is terminated by Nginx.
/// </summary>
public sealed class TrackerPublicEndpointOptions : IValidatableObject
{
    public const string SectionName = "Swarmcore:PublicEndpoint";

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
    }
}
