using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;
using System.Net;
using BeeTracker.Contracts.Admin;
using BeeTracker.Contracts.Configuration;
using BeeTracker.Contracts.Runtime;
using BeeTracker.BuildingBlocks.Abstractions.Hosting;
using BeeTracker.BuildingBlocks.Abstractions.Options;
using System.Diagnostics;
using BeeTracker.BuildingBlocks.Observability.Diagnostics;
using BeeTracker.Hosting;
using Tracker.Gateway.Application.Announce;
using Tracker.Gateway.Application.Cluster;
using Tracker.Gateway.Infrastructure;
using Tracker.Gateway.Runtime;
using Tracker.ConfigurationService.Infrastructure;
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
    }, "TrackerPublicEndpointOptions validation failed — check BeeTracker:PublicEndpoint configuration.")
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

builder.Services.AddOptions<TrackerCompatibilityOptions>()
    .Bind(builder.Configuration.GetSection(TrackerCompatibilityOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddOptions<TrackerGovernanceOptions>()
    .Bind(builder.Configuration.GetSection(TrackerGovernanceOptions.SectionName));

builder.Services.AddBeeTrackerInfrastructure(builder.Configuration, usePostgres: true, useRedis: true);
builder.Services.AddConfigurationInfrastructure(builder.Configuration);
builder.Services.AddGatewayRuntime(builder.Configuration);
builder.Services.AddGatewayInfrastructure();
builder.Services.AddGatewayClusterInfrastructure();
builder.Services.AddGatewayObservabilityServices();
builder.Services.AddUdpTracker(builder.Configuration);

// ── Startup configuration validation ──
{
    var securityOpts = builder.Configuration.GetSection(TrackerSecurityOptions.SectionName).Get<TrackerSecurityOptions>() ?? new TrackerSecurityOptions();
    var compatOpts = builder.Configuration.GetSection(TrackerCompatibilityOptions.SectionName).Get<TrackerCompatibilityOptions>() ?? new TrackerCompatibilityOptions();
    var govOpts = builder.Configuration.GetSection(TrackerGovernanceOptions.SectionName).Get<TrackerGovernanceOptions>() ?? new TrackerGovernanceOptions();
    var abuseOpts = builder.Configuration.GetSection(TrackerAbuseProtectionOptions.SectionName).Get<TrackerAbuseProtectionOptions>() ?? new TrackerAbuseProtectionOptions();
    var runtimeOpts = builder.Configuration.GetSection(GatewayRuntimeOptions.SectionName).Get<GatewayRuntimeOptions>() ?? new GatewayRuntimeOptions();
    var validation = StartupConfigurationValidator.Validate(securityOpts, compatOpts, govOpts, abuseOpts, runtimeOpts);
    var startupLogger = LoggerFactory.Create(static b => b.AddConsole()).CreateLogger("BeeTracker.Startup");
    foreach (var warning in validation.Warnings)
    {
        startupLogger.LogWarning("Config validation warning: {Warning}", warning);
    }
    foreach (var error in validation.Errors)
    {
        startupLogger.LogError("Config validation error: {Error}", error);
    }
    if (!validation.IsValid)
    {
        throw new InvalidOperationException("Startup configuration validation failed. See logged errors above.");
    }
}

var app = builder.Build();

using (var startupScope = app.Services.CreateScope())
{
    await startupScope.ServiceProvider
        .GetRequiredService<TrackerNodeConfigurationBootstrapper>()
        .InitializeAsync(CancellationToken.None);
}

var nodeConfigAccessor = app.Services.GetRequiredService<ITrackerNodeConfigurationSnapshotAccessor>();
var nodeConfig = nodeConfigAccessor.Current.Configuration;

static string RoutePrefix(string routeTemplate)
{
    var trimmed = routeTemplate.Trim();
    var parameterIndex = trimmed.IndexOf("/{", StringComparison.Ordinal);
    if (parameterIndex >= 0)
    {
        return trimmed[..parameterIndex];
    }

    return trimmed.Replace("{passkey?}", string.Empty, StringComparison.Ordinal)
        .Replace("{passkey}", string.Empty, StringComparison.Ordinal)
        .TrimEnd('/');
}

app.Use(async (context, next) =>
{
    var requestPath = context.Request.Path.Value ?? string.Empty;

    static bool TryRewrite(ref string requestPathValue, string configuredTemplate, string targetPrefix)
    {
        var configuredPrefix = RoutePrefix(configuredTemplate);
        if (string.IsNullOrWhiteSpace(configuredPrefix) ||
            string.Equals(configuredPrefix, targetPrefix, StringComparison.OrdinalIgnoreCase) ||
            !requestPathValue.StartsWith(configuredPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = requestPathValue[configuredPrefix.Length..];
        requestPathValue = $"{targetPrefix}{suffix}";
        return true;
    }

    var rewrittenPath = requestPath;
    if (requestPath.Length > 0)
    {
        if (TryRewrite(ref rewrittenPath, nodeConfig.Http.AnnounceRoute, "/announce") ||
            TryRewrite(ref rewrittenPath, nodeConfig.Http.PrivateAnnounceRoute, "/announce") ||
            TryRewrite(ref rewrittenPath, nodeConfig.Http.ScrapeRoute, "/scrape") ||
            TryRewrite(ref rewrittenPath, nodeConfig.Observability.LiveRoute, "/health/live") ||
            TryRewrite(ref rewrittenPath, nodeConfig.Observability.ReadyRoute, "/health/ready") ||
            TryRewrite(ref rewrittenPath, nodeConfig.Observability.StartupRoute, "/health/startup"))
        {
            context.Request.Path = new PathString(rewrittenPath);
        }
    }

    await next();
});

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

app.UseMiddleware<BeeTracker.Hosting.HostValidationMiddleware>();
app.UseMiddleware<BeeTracker.Hosting.PasskeyLogSanitizationMiddleware>();
app.UseMiddleware<TrackerProtocolExceptionMiddleware>();
app.UseMiddleware<TrackerRequestGuardMiddleware>();

app.MapHealthChecks("/health/live");

app.MapGet("/health/startup", (IReadinessState readiness) =>
    readiness.IsReady
        ? Results.Ok(new { status = "started" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

app.MapGet("/health/ready", async Task<IResult> (
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
    [FromServices] AdvancedAbuseGuard advancedAbuseGuard,
    [FromServices] IAnnounceService announceService,
    [FromServices] IBencodeResponseWriter bencodeResponseWriter,
    CancellationToken cancellationToken) =>
{
    var startTimestamp = Stopwatch.GetTimestamp();

    if (!parser.TryParse(httpContext, passkey, out var request, out var parseError))
    {
        TrackerDiagnostics.RequestParseFailed.Add(1, new KeyValuePair<string, object?>("endpoint", "announce"));
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        advancedAbuseGuard.RecordMalformedRequest(ip, passkey);
        await bencodeResponseWriter.WriteFailureAsync(httpContext.Response, parseError.StatusCode, parseError.FailureReason, cancellationToken);
        return;
    }

    var validation = validator.Validate(request);
    if (!validation.IsValid)
    {
        TrackerDiagnostics.RequestValidationFailed.Add(1, new KeyValuePair<string, object?>("endpoint", "announce"));
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        advancedAbuseGuard.RecordMalformedRequest(ip, request.Passkey);
        await bencodeResponseWriter.WriteFailureAsync(httpContext.Response, validation.Error.StatusCode, validation.Error.FailureReason, cancellationToken);
        return;
    }

    // Advanced abuse intelligence check
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var restriction = advancedAbuseGuard.EvaluateIp(ip);
        if (restriction == AbuseRestrictionLevel.HardBlock)
        {
            TrackerDiagnostics.AbuseIntelHardBlock.Add(1);
            await bencodeResponseWriter.WriteFailureAsync(httpContext.Response, StatusCodes.Status403Forbidden, "access denied", cancellationToken);
            return;
        }
        if (!string.IsNullOrWhiteSpace(request.Passkey))
        {
            var combinedRestriction = advancedAbuseGuard.EvaluateCombined(ip, request.Passkey);
            if (combinedRestriction == AbuseRestrictionLevel.HardBlock)
            {
                TrackerDiagnostics.AbuseIntelHardBlock.Add(1);
                await bencodeResponseWriter.WriteFailureAsync(httpContext.Response, StatusCodes.Status403Forbidden, "access denied", cancellationToken);
                return;
            }
        }
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
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        advancedAbuseGuard.RecordDeniedPolicy(ip, request.Passkey);
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
    [FromServices] AdvancedAbuseGuard advancedAbuseGuard,
    [FromServices] IScrapeService scrapeService,
    [FromServices] IBencodeResponseWriter bencodeResponseWriter,
    CancellationToken cancellationToken) =>
{
    var startTimestamp = Stopwatch.GetTimestamp();

    if (!parser.TryParse(httpContext, passkey, out var request, out var parseError))
    {
        TrackerDiagnostics.RequestParseFailed.Add(1, new KeyValuePair<string, object?>("endpoint", "scrape"));
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        advancedAbuseGuard.RecordMalformedRequest(ip, passkey);
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

    // Scrape amplification detection
    if (request.InfoHashes.Length > 40)
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        advancedAbuseGuard.RecordScrapeAmplification(ip);
    }

    // Advanced abuse intelligence check
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var restriction = advancedAbuseGuard.EvaluateIp(ip);
        if (restriction == AbuseRestrictionLevel.HardBlock)
        {
            TrackerDiagnostics.AbuseIntelHardBlock.Add(1);
            await bencodeResponseWriter.WriteFailureAsync(httpContext.Response, StatusCodes.Status403Forbidden, "access denied", cancellationToken);
            return;
        }
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
app.MapGet("/admin/nodes/{nodeId}/state", async Task<IResult> (
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
app.MapPost("/admin/nodes/{nodeId}/drain", async Task<IResult> (
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
app.MapPost("/admin/nodes/{nodeId}/maintenance", async Task<IResult> (
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
app.MapPost("/admin/nodes/{nodeId}/activate", async Task<IResult> (
    string nodeId,
    INodeOperationalStateStore nodeStateStore,
    CancellationToken cancellationToken) =>
{
    var state = new NodeOperationalStateDto(nodeId, NodeOperationalState.Active, DateTimeOffset.UtcNow);
    await nodeStateStore.SetStateAsync(state, cancellationToken);
    return Results.Ok(state);
});

// ─── Runtime Governance Controls ──────────────────────────────────────────────

app.MapGet("/admin/governance", (IRuntimeGovernanceState governanceState) =>
{
    var snapshot = governanceState.GetSnapshot();
    return Results.Ok(new GovernanceStateDto(
        snapshot.AnnounceDisabled, snapshot.ScrapeDisabled,
        snapshot.GlobalMaintenanceMode, snapshot.ReadOnlyMode,
        snapshot.EmergencyAbuseMitigation, snapshot.UdpDisabled,
        snapshot.IPv6Frozen, snapshot.PolicyFreezeMode,
        snapshot.CompatibilityMode.ToString(), snapshot.StrictnessProfile.ToString()));
});

app.MapPost("/admin/governance", (
    GovernanceUpdateRequest request,
    IRuntimeGovernanceState governanceState) =>
{
    ClientCompatibilityMode? compatMode = null;
    if (request.CompatibilityMode is not null &&
        Enum.TryParse<ClientCompatibilityMode>(request.CompatibilityMode, ignoreCase: true, out var parsedMode))
    {
        compatMode = parsedMode;
    }

    ProtocolStrictnessProfile? strictnessProfile = null;
    if (request.StrictnessProfile is not null &&
        Enum.TryParse<ProtocolStrictnessProfile>(request.StrictnessProfile, ignoreCase: true, out var parsedProfile))
    {
        strictnessProfile = parsedProfile;
    }

    governanceState.Apply(new RuntimeGovernanceUpdate(
        request.AnnounceDisabled, request.ScrapeDisabled,
        request.GlobalMaintenanceMode, request.ReadOnlyMode,
        request.EmergencyAbuseMitigation, request.UdpDisabled,
        request.IPv6Frozen, request.PolicyFreezeMode,
        compatMode, strictnessProfile));

    TrackerDiagnostics.GovernanceStateChanges.Add(1);

    var snapshot = governanceState.GetSnapshot();
    return Results.Ok(new GovernanceStateDto(
        snapshot.AnnounceDisabled, snapshot.ScrapeDisabled,
        snapshot.GlobalMaintenanceMode, snapshot.ReadOnlyMode,
        snapshot.EmergencyAbuseMitigation, snapshot.UdpDisabled,
        snapshot.IPv6Frozen, snapshot.PolicyFreezeMode,
        snapshot.CompatibilityMode.ToString(), snapshot.StrictnessProfile.ToString()));
});

// ─── Abuse Intelligence Diagnostics ──────────────────────────────────────────

app.MapGet("/admin/abuse/diagnostics", (AdvancedAbuseGuard abuseGuard) =>
{
    var summary = abuseGuard.GetSummary();
    var topOffenders = abuseGuard.GetDiagnostics(50);
    return Results.Ok(new AbuseDiagnosticsDto(
        summary.TrackedIps, summary.TrackedPasskeys,
        summary.WarnedCount, summary.SoftRestrictedCount, summary.HardBlockedCount,
        topOffenders.Select(static e => new AbuseDiagnosticsEntryDto(
            e.Key, e.KeyType, e.MalformedRequestCount, e.DeniedPolicyCount,
            e.PeerIdAnomalyCount, e.SuspiciousPatternCount,
            e.ScrapeAmplificationCount, e.TotalScore, e.RestrictionLevel)).ToArray()));
});

// ─── Runtime Diagnostics ─────────────────────────────────────────────────────

app.MapGet("/admin/diagnostics", (
    IRuntimeGovernanceState governanceState,
    AdvancedAbuseGuard abuseGuard,
    IAnnounceTelemetryWriter telemetryWriter,
    IRuntimeSwarmStore runtimeSwarmStore,
    IOptions<TrackerNodeOptions> nodeOptions) =>
{
    var govSnapshot = governanceState.GetSnapshot();
    var govDto = new GovernanceStateDto(
        govSnapshot.AnnounceDisabled, govSnapshot.ScrapeDisabled,
        govSnapshot.GlobalMaintenanceMode, govSnapshot.ReadOnlyMode,
        govSnapshot.EmergencyAbuseMitigation, govSnapshot.UdpDisabled,
        govSnapshot.IPv6Frozen, govSnapshot.PolicyFreezeMode,
        govSnapshot.CompatibilityMode.ToString(), govSnapshot.StrictnessProfile.ToString());

    var abuseSummary = abuseGuard.GetSummary();
    var abuseDto = new AbuseDiagnosticsDto(
        abuseSummary.TrackedIps, abuseSummary.TrackedPasskeys,
        abuseSummary.WarnedCount, abuseSummary.SoftRestrictedCount, abuseSummary.HardBlockedCount,
        Array.Empty<AbuseDiagnosticsEntryDto>());

    var configIssues = nodeConfigAccessor.Validation.Issues;
    var configDto = new ConfigValidationDto(
        nodeConfigAccessor.Validation.IsValid,
        configIssues.Where(static issue => issue.Severity == TrackerConfigValidationSeverity.Error).Select(static issue => issue.Message).ToArray(),
        configIssues.Where(static issue => issue.Severity == TrackerConfigValidationSeverity.Warning).Select(static issue => issue.Message).ToArray());

    var store = (PartitionedRuntimeSwarmStore)runtimeSwarmStore;
    var overview = new TrackerOverviewDto(
        nodeOptions.Value.NodeId,
        (int)store.GetTotalSwarmCount(),
        (int)store.GetTotalPeerCount(),
        telemetryWriter.QueueLength,
        [new CacheStatusDto("L1", "access", true), new CacheStatusDto("L2", "access", true)]);

    return Results.Ok(new RuntimeDiagnosticsDto(govDto, abuseDto, configDto, overview));
});

// ─── Config Validation Endpoint ──────────────────────────────────────────────

app.MapGet("/admin/config/validate", (
    ITrackerNodeConfigurationSnapshotAccessor accessor) =>
{
    var issues = accessor.Validation.Issues;
    return Results.Ok(new ConfigValidationDto(
        accessor.Validation.IsValid,
        issues.Where(static issue => issue.Severity == TrackerConfigValidationSeverity.Error).Select(static issue => issue.Message).ToArray(),
        issues.Where(static issue => issue.Severity == TrackerConfigValidationSeverity.Warning).Select(static issue => issue.Message).ToArray()));
});

app.MapGet("/admin/node/config", (ITrackerNodeConfigurationSnapshotAccessor accessor)
    => Results.Ok(accessor.Current));

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Run();

public partial class Program;
