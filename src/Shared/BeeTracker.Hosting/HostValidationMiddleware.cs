using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using BeeTracker.BuildingBlocks.Abstractions.Hosting;

namespace BeeTracker.Hosting;

/// <summary>
/// Validates the Host header (which reflects X-Forwarded-Host after ForwardedHeaders processing)
/// against the configured allowed hosts. Returns 400 for unrecognized hosts.
/// Must run after <c>UseForwardedHeaders()</c> so that <c>Request.Host</c> has been rewritten.
/// </summary>
public sealed class HostValidationMiddleware(
    RequestDelegate next,
    IHostValidationService hostValidationService,
    ILogger<HostValidationMiddleware> logger)
{
    public Task InvokeAsync(HttpContext httpContext)
    {
        var host = httpContext.Request.Host.Value;

        if (hostValidationService.IsAllowed(host))
        {
            return next(httpContext);
        }

        logger.LogWarning("Rejected request with unallowed Host header: {Host}", host ?? "(empty)");
        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        return Task.CompletedTask;
    }
}
