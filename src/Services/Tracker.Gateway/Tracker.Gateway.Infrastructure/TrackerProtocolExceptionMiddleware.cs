using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Tracker.Gateway.Infrastructure;

public sealed class TrackerProtocolExceptionMiddleware(
    RequestDelegate next,
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
            logger.LogError(exception, "Unhandled tracker protocol exception for {Path}.", httpContext.Request.Path);

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
                logger.LogError(writeException, "Failed to write error response for {Path}.", httpContext.Request.Path);
            }
        }
    }

    private static bool IsTrackerProtocolRequest(PathString path)
    {
        return path.StartsWithSegments("/announce", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/scrape", StringComparison.OrdinalIgnoreCase);
    }
}
