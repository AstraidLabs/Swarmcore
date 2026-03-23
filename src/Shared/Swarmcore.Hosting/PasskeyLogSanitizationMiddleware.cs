using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Swarmcore.Hosting;

/// <summary>
/// Intercepts ASP.NET Core request logging to sanitize passkey values from the logged path.
/// Replaces the last path segment in /announce/{passkey} and /scrape/{passkey} paths
/// with "***" in the <see cref="HttpContext.Items"/> used by diagnostic logging.
/// Routing is never affected — only the log-visible representation changes.
/// </summary>
public sealed class PasskeyLogSanitizationMiddleware(RequestDelegate next)
{
    // Matches /announce/<segment> or /scrape/<segment> where <segment> is the passkey.
    private static readonly Regex PasskeyPathPattern = new(
        @"^/(announce|scrape)/([^/]+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public Task InvokeAsync(HttpContext httpContext)
    {
        var path = httpContext.Request.Path.Value;
        if (!string.IsNullOrEmpty(path) && PasskeyPathPattern.IsMatch(path))
        {
            // Store the sanitized path for diagnostic/logging purposes.
            // ASP.NET Core's built-in request logging reads Request.Path; we cannot change it
            // without breaking routing. Instead, downstream loggers can read this item.
            var sanitized = PasskeyPathPattern.Replace(path, "/$1/***");
            httpContext.Items["SanitizedPath"] = sanitized;
        }

        return next(httpContext);
    }

    /// <summary>
    /// Returns the sanitized path for logging. Falls back to the original path if not sanitized.
    /// </summary>
    public static string GetLoggablePath(HttpContext httpContext)
    {
        if (httpContext.Items.TryGetValue("SanitizedPath", out var sanitized) && sanitized is string s)
        {
            return s;
        }

        return httpContext.Request.Path.Value ?? "/";
    }
}
