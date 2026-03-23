namespace Swarmcore.BuildingBlocks.Abstractions.Hosting;

/// <summary>
/// Validates incoming Host headers against the configured allowed hosts and subdomains.
/// Prevents host header injection when the application runs behind a reverse proxy.
/// </summary>
public interface IHostValidationService
{
    /// <summary>
    /// Returns true if the given host (from Host or X-Forwarded-Host header) is allowed.
    /// </summary>
    bool IsAllowed(string? host);

    /// <summary>
    /// Extracts the subdomain prefix from a host value given the configured BaseDomain.
    /// Returns null if the host is not a subdomain of BaseDomain.
    /// </summary>
    string? GetSubdomain(string? host);
}
