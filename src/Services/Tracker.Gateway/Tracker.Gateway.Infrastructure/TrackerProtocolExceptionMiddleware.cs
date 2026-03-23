using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Tracker.Gateway.Application.Announce;

namespace Tracker.Gateway.Infrastructure;

public sealed class TrackerProtocolExceptionMiddleware(
    RequestDelegate next,
    IPasskeyRedactor passkeyRedactor,
    ILogger<TrackerProtocolExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext httpContext, IBencodeResponseWriter bencodeResponseWriter)
    {
        try
        {
            await next(httpContext);
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsTrackerProtocolRequest(httpContext.Request.Path))
        {
            // Redact passkey from path before logging — passkeys are sensitive credentials.
            var redactedPath = RedactPath(httpContext.Request.Path, passkeyRedactor);
            logger.LogError(exception, "Unhandled tracker protocol exception for {Path}.", redactedPath);

            if (httpContext.Response.HasStarted)
            {
                throw;
            }

            try
            {
                httpContext.Response.Clear();
                await bencodeResponseWriter.WriteFailureAsync(
                    httpContext.Response,
                    StatusCodes.Status500InternalServerError,
                    "internal server error",
                    httpContext.RequestAborted);
            }
            catch (Exception writeException)
            {
                logger.LogError(writeException, "Failed to write error response for {Path}.", redactedPath);
            }
        }
    }

    private static bool IsTrackerProtocolRequest(PathString path)
    {
        return path.StartsWithSegments("/announce", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/scrape", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Redacts the passkey segment from tracker paths.
    /// /announce/abcdef1234 → /announce/abc***def
    /// </summary>
    private static string RedactPath(PathString path, IPasskeyRedactor passkeyRedactor)
    {
        var value = path.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            return "/";
        }

        var lastSlash = value.LastIndexOf('/');
        if (lastSlash <= 0 || lastSlash == value.Length - 1)
        {
            return value;
        }

        var candidate = value[(lastSlash + 1)..];
        return $"{value[..(lastSlash + 1)]}{passkeyRedactor.Redact(candidate)}";
    }
}
