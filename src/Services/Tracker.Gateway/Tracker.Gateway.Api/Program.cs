using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Net;
using Swarmcore.Contracts.Admin;
using Swarmcore.BuildingBlocks.Abstractions.Hosting;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using System.Diagnostics;
using Swarmcore.BuildingBlocks.Observability.Diagnostics;
using Swarmcore.Hosting;
using Tracker.Gateway.Application.Announce;
using Tracker.Gateway.Infrastructure;
using Tracker.Gateway.Runtime;
using Tracker.UdpTracker.Service;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddOptions<ForwardedHeadersOptions>()
    .Configure<IOptions<TrustedProxyOptions>>((options, trustedProxyOptionsAccessor) =>
{
    var trustedProxyOptions = trustedProxyOptionsAccessor.Value;
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = trustedProxyOptions.ForwardLimit;
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();

    foreach (var proxy in trustedProxyOptions.KnownProxies)
    {
        if (IPAddress.TryParse(proxy, out var ipAddress))
        {
            options.KnownProxies.Add(ipAddress);
        }
    }

    foreach (var network in trustedProxyOptions.KnownNetworks)
    {
        var separator = network.IndexOf('/');
        if (separator <= 0 || separator >= network.Length - 1)
        {
            continue;
        }

        if (IPAddress.TryParse(network[..separator], out var prefix) && int.TryParse(network[(separator + 1)..], out var prefixLength))
        {
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(prefix, prefixLength));
        }
    }
});

builder.Services.AddSwarmcoreInfrastructure(builder.Configuration, usePostgres: true, useRedis: true);
builder.Services.AddGatewayRuntime(builder.Configuration);
builder.Services.AddGatewayInfrastructure();
builder.Services.AddGatewayObservabilityServices();
builder.Services.AddUdpTracker(builder.Configuration);

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<TrackerProtocolExceptionMiddleware>();
app.UseMiddleware<TrackerRequestGuardMiddleware>();

app.MapHealthChecks("/health/live");
app.MapGet("/health/startup", (IReadinessState readiness) =>
    readiness.IsReady
        ? Results.Ok(new { status = "started" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

app.MapGet("/health/ready", (
    IReadinessState readiness,
    IGatewayDependencyState dependencyState,
    IOptions<DependencyDegradationOptions> degradationOptions) =>
{
    var snapshot = dependencyState.Snapshot;
    var isReady = readiness.IsReady
        && (!degradationOptions.Value.RequireRedisForReadiness || snapshot.Redis.IsHealthy)
        && (!degradationOptions.Value.RequirePostgresForReadiness || snapshot.Postgres.IsHealthy);

    return isReady
        ? Results.Ok(new
        {
            status = "ready",
            dependencies = new
            {
                redis = snapshot.Redis,
                postgres = snapshot.Postgres
            }
        })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
});

app.MapGet("/announce/{passkey?}", async Task (
    HttpContext httpContext,
    string? passkey,
    [FromServices] IAnnounceRequestParser parser,
    [FromServices] IAnnounceRequestValidator validator,
    [FromServices] IAnnounceAbuseGuard abuseGuard,
    [FromServices] IAnnounceService announceService,
    [FromServices] IBencodeResponseWriter bencodeResponseWriter,
    CancellationToken cancellationToken) =>
{
    var startTimestamp = Stopwatch.GetTimestamp();

    if (!parser.TryParse(httpContext, passkey, out var request, out var parseError))
    {
        TrackerDiagnostics.RequestParseFailed.Add(1, new KeyValuePair<string, object?>("endpoint", "announce"));
        await bencodeResponseWriter.WriteFailureAsync(httpContext.Response, parseError.StatusCode, parseError.FailureReason, cancellationToken);
        return;
    }

    var validation = validator.Validate(request);
    if (!validation.IsValid)
    {
        TrackerDiagnostics.RequestValidationFailed.Add(1, new KeyValuePair<string, object?>("endpoint", "announce"));
        await bencodeResponseWriter.WriteFailureAsync(httpContext.Response, validation.Error.StatusCode, validation.Error.FailureReason, cancellationToken);
        return;
    }

    if (abuseGuard.Evaluate(httpContext, request) is { } abuseError)
    {
        TrackerDiagnostics.AnnounceDenied.Add(1, new KeyValuePair<string, object?>("reason", abuseError.FailureReason));
        await bencodeResponseWriter.WriteFailureAsync(httpContext.Response, abuseError.StatusCode, abuseError.FailureReason, cancellationToken);
        return;
    }

    var (success, error) = await announceService.ExecuteAsync(request, cancellationToken);
    if (error is { } announceError)
    {
        TrackerDiagnostics.AnnounceDenied.Add(1, new KeyValuePair<string, object?>("reason", announceError.FailureReason));
        await bencodeResponseWriter.WriteFailureAsync(httpContext.Response, announceError.StatusCode, announceError.FailureReason, cancellationToken);
        return;
    }

    TrackerDiagnostics.AnnounceTotal.Add(1, new KeyValuePair<string, object?>("event", request.Event.ToString().ToLowerInvariant()));

    var response = success!.Value;
    try
    {
        await bencodeResponseWriter.WriteAnnounceSuccessAsync(httpContext.Response, response, cancellationToken);
    }
    finally
    {
        response.PeerSelection.Dispose();
        TrackerDiagnostics.AnnounceDuration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
    }
}).ExcludeFromDescription();

app.MapGet("/scrape/{passkey?}", async Task (
    HttpContext httpContext,
    string? passkey,
    [FromServices] IScrapeRequestParser parser,
    [FromServices] IScrapeRequestValidator validator,
    [FromServices] IScrapeAbuseGuard scrapeAbuseGuard,
    [FromServices] IScrapeService scrapeService,
    [FromServices] IBencodeResponseWriter bencodeResponseWriter,
    CancellationToken cancellationToken) =>
{
    var startTimestamp = Stopwatch.GetTimestamp();

    if (!parser.TryParse(httpContext, passkey, out var request, out var parseError))
    {
        TrackerDiagnostics.RequestParseFailed.Add(1, new KeyValuePair<string, object?>("endpoint", "scrape"));
        await bencodeResponseWriter.WriteFailureAsync(httpContext.Response, parseError.StatusCode, parseError.FailureReason, cancellationToken);
        return;
    }

    var validation = validator.Validate(request);
    if (!validation.IsValid)
    {
        TrackerDiagnostics.RequestValidationFailed.Add(1, new KeyValuePair<string, object?>("endpoint", "scrape"));
        await bencodeResponseWriter.WriteFailureAsync(httpContext.Response, validation.Error.StatusCode, validation.Error.FailureReason, cancellationToken);
        return;
    }

    if (scrapeAbuseGuard.Evaluate(httpContext) is { } scrapeAbuseError)
    {
        TrackerDiagnostics.ScrapeDenied.Add(1);
        await bencodeResponseWriter.WriteFailureAsync(httpContext.Response, scrapeAbuseError.StatusCode, scrapeAbuseError.FailureReason, cancellationToken);
        return;
    }

    var (success, error) = await scrapeService.ExecuteAsync(request, cancellationToken);
    if (error is { } scrapeError)
    {
        TrackerDiagnostics.ScrapeDenied.Add(1);
        await bencodeResponseWriter.WriteFailureAsync(httpContext.Response, scrapeError.StatusCode, scrapeError.FailureReason, cancellationToken);
        return;
    }

    TrackerDiagnostics.ScrapeTotal.Add(1);
    await bencodeResponseWriter.WriteScrapeSuccessAsync(httpContext.Response, success!.Value, cancellationToken);
    TrackerDiagnostics.ScrapeDuration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
}).ExcludeFromDescription();

app.MapGet("/admin/overview", (
    IAnnounceTelemetryWriter telemetryWriter,
    IRuntimeSwarmStore runtimeSwarmStore,
    IOptions<TrackerNodeOptions> nodeOptions) =>
{
    var store = (PartitionedRuntimeSwarmStore)runtimeSwarmStore;
    var overview = new TrackerOverviewDto(
        nodeOptions.Value.NodeId,
        (int)store.GetTotalSwarmCount(),
        (int)store.GetTotalPeerCount(),
        telemetryWriter.QueueLength,
        [new CacheStatusDto("L1", "access", true), new CacheStatusDto("L2", "access", true)]);

    return Results.Ok(overview);
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Run();

public partial class Program;
