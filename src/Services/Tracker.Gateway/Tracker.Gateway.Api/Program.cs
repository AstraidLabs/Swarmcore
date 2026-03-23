using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;
using System.Net;
using Swarmcore.Contracts.Admin;
using Swarmcore.Contracts.Runtime;
using Swarmcore.BuildingBlocks.Abstractions.Hosting;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using System.Diagnostics;
using Swarmcore.BuildingBlocks.Observability.Diagnostics;
using Swarmcore.Hosting;
using Tracker.Gateway.Application.Announce;
using Tracker.Gateway.Application.Cluster;
using Tracker.Gateway.Infrastructure;
using Tracker.Gateway.Runtime;
using Tracker.UdpTracker.Service;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddOptions<TrackerPublicEndpointOptions>()
    .Bind(builder.Configuration.GetSection(TrackerPublicEndpointOptions.SectionName))
    .Validate(static options =>
    {
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        return System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            options,
            new System.ComponentModel.DataAnnotations.ValidationContext(options),
            results,
            validateAllProperties: true);
    }, "TrackerPublicEndpointOptions validation failed — check Swarmcore:PublicEndpoint configuration.")
    .ValidateOnStart();

builder.Services.AddOptions<ForwardedHeadersOptions>()
    .Configure<IOptions<TrustedProxyOptions>>((options, trustedProxyOptionsAccessor) =>
{
    var trustedProxyOptions = trustedProxyOptionsAccessor.Value;
    // Include X-Forwarded-Host so Request.Host reflects the public hostname after proxy.
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
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
builder.Services.AddGatewayClusterInfrastructure();
builder.Services.AddGatewayObservabilityServices();
builder.Services.AddUdpTracker(builder.Configuration);

var app = builder.Build();

// ForwardedHeaders MUST be the first middleware so that all subsequent middleware
// (HSTS, routing, auth) sees the correct scheme/host/IP from the proxy.
app.UseForwardedHeaders();

// HSTS — opt-in via config. Nginx is the recommended place to emit this header.
// Enable here only when the application is directly internet-facing.
var publicEndpointOptions = app.Services.GetRequiredService<IOptions<TrackerPublicEndpointOptions>>().Value;
if (publicEndpointOptions.EnableHsts)
{
    app.UseHsts();
}

// HTTPS redirect — opt-in via config. Keep disabled (default) when Nginx handles it.
// Enabling inside a reverse-proxy without careful ForwardedHeaders setup causes redirect loops.
if (publicEndpointOptions.EnableHttpsRedirection)
{
    app.UseHttpsRedirection();
}

app.UseMiddleware<TrackerProtocolExceptionMiddleware>();
app.UseMiddleware<TrackerRequestGuardMiddleware>();

app.MapHealthChecks("/health/live");

app.MapGet("/health/startup", (IReadinessState readiness) =>
    readiness.IsReady
        ? Results.Ok(new { status = "started" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

app.MapGet("/health/ready", async Task (
    IReadinessState readiness,
    IGatewayDependencyState dependencyState,
    INodeOperationalStateStore nodeStateStore,
    IOptions<DependencyDegradationOptions> degradationOptions,
    IOptions<ClusterShardingOptions> shardingOptions,
    IOptions<TrackerNodeOptions> nodeOptions,
    CancellationToken cancellationToken) =>
{
    var snapshot = dependencyState.Snapshot;
    var isReady = readiness.IsReady
        && (!degradationOptions.Value.RequireRedisForReadiness || snapshot.Redis.IsHealthy)
        && (!degradationOptions.Value.RequirePostgresForReadiness || snapshot.Postgres.IsHealthy);

    if (!isReady)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    // Fail readiness when draining or in maintenance, so the load balancer stops routing to this node.
    // Non-ready does not mean unhealthy — it signals graceful removal from rotation.
    if (shardingOptions.Value.FailReadinessWhenDraining)
    {
        var nodeState = await nodeStateStore.GetStateAsync(nodeOptions.Value.NodeId, cancellationToken);
        if (nodeState is { State: NodeOperationalState.Draining or NodeOperationalState.Maintenance })
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    return Results.Ok(new
    {
        status = "ready",
        dependencies = new
        {
            redis = snapshot.Redis,
            postgres = snapshot.Postgres
        }
    });
});

// ─── Tracker Protocol Endpoints ───────────────────────────────────────────────

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

// ─── Node Runtime Overview ────────────────────────────────────────────────────

app.MapGet("/admin/overview", (
    IAnnounceTelemetryWriter telemetryWriter,
    IRuntimeSwarmStore runtimeSwarmStore,
    IShardRouter shardRouter,
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

// ─── Cluster Shard Diagnostics ────────────────────────────────────────────────

/// <summary>
/// Returns the shard ownership map as seen by this node's local ownership cache.
/// Reflects the state from the last ownership cache refresh (default every 15s).
/// </summary>
app.MapGet("/admin/cluster/shards", (
    IShardRouter shardRouter,
    IOptions<ClusterShardingOptions> shardingOptions) =>
{
    var total = shardingOptions.Value.ClusterShardCount;
    var snapshot = shardRouter.GetOwnershipSnapshot();
    var shards = new ClusterShardOwnershipDto[total];

    for (var i = 0; i < total; i++)
    {
        snapshot.TryGetValue(i, out var ownerNodeId);
        shards[i] = new ClusterShardOwnershipDto(
            i,
            ownerNodeId,
            ownerNodeId is not null && shardRouter.IsLocallyOwned(i),
            LeaseExpiresAtUtc: null); // TTL managed in Redis; not surfaced here
    }

    var owned = shards.Count(static s => s.OwnerNodeId is not null);
    var result = new ClusterShardDiagnosticsDto(
        DateTimeOffset.UtcNow,
        total,
        owned,
        total - owned,
        shards);

    return Results.Ok(result);
});

/// <summary>
/// Returns ownership info for a single cluster shard.
/// </summary>
app.MapGet("/admin/cluster/shards/{shardId:int}", (
    int shardId,
    IShardRouter shardRouter,
    IOptions<ClusterShardingOptions> shardingOptions) =>
{
    if (shardId < 0 || shardId >= shardingOptions.Value.ClusterShardCount)
    {
        return Results.BadRequest(new { error = "shard_id out of range" });
    }

    var ownerNodeId = shardRouter.GetOwnerNodeId(shardId);
    var dto = new ClusterShardOwnershipDto(
        shardId,
        ownerNodeId,
        ownerNodeId is not null && shardRouter.IsLocallyOwned(shardId),
        LeaseExpiresAtUtc: null);

    return Results.Ok(dto);
});

// ─── Node Operational State Controls ─────────────────────────────────────────

/// <summary>
/// Returns the current operational state of the specified node.
/// Used by operators and automation to check drain/maintenance status.
/// </summary>
app.MapGet("/admin/nodes/{nodeId}/state", async Task (
    string nodeId,
    INodeOperationalStateStore nodeStateStore,
    CancellationToken cancellationToken) =>
{
    var state = await nodeStateStore.GetStateAsync(nodeId, cancellationToken);
    if (state is null)
    {
        return Results.Ok(new NodeOperationalStateDto(nodeId, NodeOperationalState.Active, DateTimeOffset.UtcNow));
    }

    return Results.Ok(state);
});

/// <summary>
/// Transitions the specified node to Draining state.
/// The node's readiness probe will start failing, removing it from the load balancer pool.
/// Owned shards will be released on graceful shutdown.
/// </summary>
app.MapPost("/admin/nodes/{nodeId}/drain", async Task (
    string nodeId,
    INodeOperationalStateStore nodeStateStore,
    IOptions<TrackerNodeOptions> nodeOptions,
    CancellationToken cancellationToken) =>
{
    var state = new NodeOperationalStateDto(nodeId, NodeOperationalState.Draining, DateTimeOffset.UtcNow);
    await nodeStateStore.SetStateAsync(state, cancellationToken);
    return Results.Ok(state);
});

/// <summary>
/// Transitions the specified node to Maintenance state.
/// Similar to Drain but signals a planned extended outage.
/// </summary>
app.MapPost("/admin/nodes/{nodeId}/maintenance", async Task (
    string nodeId,
    INodeOperationalStateStore nodeStateStore,
    CancellationToken cancellationToken) =>
{
    var state = new NodeOperationalStateDto(nodeId, NodeOperationalState.Maintenance, DateTimeOffset.UtcNow);
    await nodeStateStore.SetStateAsync(state, cancellationToken);
    return Results.Ok(state);
});

/// <summary>
/// Returns the specified node to Active state, re-enabling readiness and ownership claims.
/// </summary>
app.MapPost("/admin/nodes/{nodeId}/activate", async Task (
    string nodeId,
    INodeOperationalStateStore nodeStateStore,
    CancellationToken cancellationToken) =>
{
    var state = new NodeOperationalStateDto(nodeId, NodeOperationalState.Active, DateTimeOffset.UtcNow);
    await nodeStateStore.SetStateAsync(state, cancellationToken);
    return Results.Ok(state);
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Run();

public partial class Program;
