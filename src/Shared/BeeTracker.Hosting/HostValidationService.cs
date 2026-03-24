using Microsoft.Extensions.Options;
using BeeTracker.BuildingBlocks.Abstractions.Hosting;
using BeeTracker.BuildingBlocks.Abstractions.Options;

namespace BeeTracker.Hosting;

/// <summary>
/// Validates incoming Host values against <see cref="TrackerPublicEndpointOptions.AllowedHosts"/>
/// and subdomain rules. Returns false for hosts not explicitly allowed, preventing host header injection.
/// When <see cref="TrackerPublicEndpointOptions.AllowedHosts"/> is empty, validation is disabled
/// (all hosts pass) for backward compatibility and development scenarios.
/// </summary>
public sealed class HostValidationService : IHostValidationService
{
    private readonly HashSet<string> _allowedHosts;
    private readonly HashSet<string> _allowedSubdomains;
    private readonly string _baseDomain;
    private readonly bool _wildcardSubdomains;
    private readonly bool _validationEnabled;

    public HostValidationService(IOptions<TrackerPublicEndpointOptions> options)
    {
        var config = options.Value;
        _baseDomain = (config.BaseDomain ?? string.Empty).Trim().ToLowerInvariant();
        _wildcardSubdomains = config.EnableWildcardSubdomains;

        _allowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var host in config.AllowedHosts)
        {
            if (!string.IsNullOrWhiteSpace(host))
            {
                _allowedHosts.Add(host.Trim());
            }
        }

        _allowedSubdomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var subdomain in config.AllowedSubdomains)
        {
            if (!string.IsNullOrWhiteSpace(subdomain))
            {
                _allowedSubdomains.Add(subdomain.Trim());
            }
        }

        // Also derive allowed hosts from per-surface URLs if AllowedHosts list is empty.
        if (_allowedHosts.Count == 0)
        {
            AddHostFromUrl(config.BaseUrl);
            AddHostFromUrl(config.AnnounceBaseUrl);
            AddHostFromUrl(config.ScrapeBaseUrl);
            AddHostFromUrl(config.AdminBaseUrl);
            AddHostFromUrl(config.ApiBaseUrl);
        }

        _validationEnabled = _allowedHosts.Count > 0;
    }

    public bool IsAllowed(string? host)
    {
        if (!_validationEnabled)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        // Strip port if present (e.g. "admin.example.com:8443" → "admin.example.com").
        var hostOnly = StripPort(host);

        if (_allowedHosts.Contains(hostOnly))
        {
            return true;
        }

        // Check subdomain rules when BaseDomain is configured.
        if (string.IsNullOrEmpty(_baseDomain))
        {
            return false;
        }

        var subdomain = ExtractSubdomain(hostOnly);
        if (subdomain is null)
        {
            return false;
        }

        if (_wildcardSubdomains)
        {
            return true;
        }

        return _allowedSubdomains.Contains(subdomain);
    }

    public string? GetSubdomain(string? host)
    {
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrEmpty(_baseDomain))
        {
            return null;
        }

        return ExtractSubdomain(StripPort(host));
    }

    private string? ExtractSubdomain(string hostOnly)
    {
        var suffix = $".{_baseDomain}";
        if (!hostOnly.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var prefix = hostOnly[..^suffix.Length];
        if (string.IsNullOrEmpty(prefix))
        {
            return null;
        }

        return prefix;
    }

    private static string StripPort(string host)
    {
        var colonIndex = host.LastIndexOf(':');
        if (colonIndex < 0)
        {
            return host;
        }

        // Avoid stripping IPv6 bracket notation.
        if (host.Contains('['))
        {
            return host;
        }

        return host[..colonIndex];
    }

    private void AddHostFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return;
        }

        _allowedHosts.Add(uri.Host);
    }
}
