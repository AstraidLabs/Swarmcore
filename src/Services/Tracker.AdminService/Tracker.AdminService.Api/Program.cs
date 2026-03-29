using MediatR;
using System.Security.Claims;
using System.Net;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using BeeTracker.BuildingBlocks.Abstractions.Hosting;
using BeeTracker.BuildingBlocks.Abstractions.Options;
using BeeTracker.BuildingBlocks.Application.Queries;
using BeeTracker.BuildingBlocks.Observability.Diagnostics;
using BeeTracker.Contracts.Configuration;
using BeeTracker.Hosting;
using Tracker.ConfigurationService.Application;
using Tracker.AdminService.Application;
using Tracker.AdminService.Api;
using Tracker.AdminService.Api.Hubs;
using Tracker.AdminService.Infrastructure;
using Audit.Infrastructure;
using Notification.Infrastructure;
using Identity.SelfService.Infrastructure;
using Identity.SelfService.Application;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddOptions<TrackerPublicEndpointOptions>()
    .Bind(builder.Configuration.GetSection(TrackerPublicEndpointOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddOptions<ForwardedHeadersOptions>()
    .Configure<IOptions<TrustedProxyOptions>>((options, trustedProxyOptionsAccessor) =>
{
    var trustedProxyOptions = trustedProxyOptionsAccessor.Value;
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
builder.Services.AddBeeTrackerInfrastructure(builder.Configuration, usePostgres: true, useRedis: true);
builder.Services.AddAdminApplication();
builder.Services.AddAdminInfrastructure(builder.Configuration);
builder.Services.AddAdminAuthInfrastructure(builder.Configuration);
builder.Services.AddAdminApiAuthentication(builder.Configuration);
builder.Services.AddAuditInfrastructure(builder.Configuration);
builder.Services.AddNotificationInfrastructure(builder.Configuration);
builder.Services.AddSelfServiceInfrastructure();
builder.Services.AddMediatR(
    typeof(Identity.SelfService.Application.RegisterAdminCommand).Assembly,
    typeof(Audit.Application.AuditEventNotification).Assembly);
builder.Services.AddSignalR();
builder.Services.AddHostedService<LiveStatsPublisher>();
builder.Services.AddHostedService<AdminStartupService>();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<BeeTracker.Hosting.HostValidationMiddleware>();
app.UseMiddleware<BeeTracker.Hosting.PasskeyLogSanitizationMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (await AdminAntiforgery.ValidateUnsafeAdminRequestAsync(context))
    {
        await next();
    }
});
app.MapCommonHealthEndpoints();
app.MapHub<LiveStatsHub>("/hubs/live-stats");
app.MapGet("/account/login", (Delegate)AdminTokenEndpoint.RenderLoginPage);
app.MapPost("/account/login", (Delegate)AdminTokenEndpoint.HandleLoginPostAsync);
app.MapPost("/account/logout", (Delegate)AdminTokenEndpoint.HandleLogoutPostAsync);
app.MapGet("/connect/authorize", (Delegate)AdminTokenEndpoint.HandleAuthorizationAsync);
app.MapPost("/connect/authorize", (Delegate)AdminTokenEndpoint.HandleAuthorizationAsync);
app.MapPost("/connect/token", (Delegate)AdminTokenEndpoint.HandleAsync);
app.MapGet("/admin-ui/config", (HttpContext httpContext, IOptions<AdminIdentityOptions> adminIdentityOptionsAccessor) =>
{
    var options = adminIdentityOptionsAccessor.Value;
    // After UseForwardedHeaders(), Request.Scheme and Request.Host already reflect
    // the proxy headers (X-Forwarded-Proto, X-Forwarded-Host). No need to read raw headers.
    var scheme = httpContext.Request.Scheme;
    var host = httpContext.Request.Host.Value ?? string.Empty;
    var origin = $"{scheme}://{host}";
    return Results.Ok(new AdminUiClientConfigurationResponse(
        origin,
        options.SpaClientId,
        $"{origin}{options.SpaRedirectPath}",
        $"{origin}{options.SpaPostLogoutPath}",
        $"openid profile roles {options.AdminApiScope}",
        "code"));
});

app.MapSelfServiceEndpoints();
app.MapRbacEndpoints();

var adminApi = app.MapGroup("/api/admin");

adminApi.MapGet("/session", async (HttpContext httpContext, IAntiforgery antiforgery, Microsoft.Extensions.Options.IOptions<AdminIdentityOptions> adminIdentityOptionsAccessor, TimeProvider timeProvider, IRbacService rbacService) =>
{
    var returnUrl = AdminReauthentication.ResolveReturnUrl(httpContext);
    var reauthenticationUrl = AdminReauthentication.BuildLoginPath(returnUrl);

    if (httpContext.User.Identity?.IsAuthenticated != true)
    {
        var anonymousCapabilities = AdminSessionCapabilitiesBuilder.Build(new HashSet<string>(StringComparer.Ordinal));
        return Results.Ok(new AdminSessionResponse(
            false,
            string.Empty,
            string.Empty,
            Array.Empty<string>(),
            string.Empty,
            null,
            false,
            reauthenticationUrl,
            anonymousCapabilities,
            AdminReauthentication.BuildContext(
                reason: AdminReauthenticationReasons.SessionMissing,
                action: "admin.session.bootstrap",
                returnUrl,
                reauthenticationUrl,
                severity: AdminReauthenticationSeverities.Low)));
    }

    var sessionVersion = AdminSessionState.TryGetPermissionSnapshotVersion(httpContext.User);
    var currentVersion = await rbacService.GetPermissionSnapshotVersionAsync(httpContext.RequestAborted);
    if (sessionVersion is null || sessionVersion.Value != currentVersion)
    {
        return Results.Ok(new AdminSessionResponse(
            false,
            string.Empty,
            string.Empty,
            Array.Empty<string>(),
            string.Empty,
            null,
            true,
            reauthenticationUrl,
            AdminSessionCapabilitiesBuilder.Build(new HashSet<string>(StringComparer.Ordinal)),
            AdminReauthentication.BuildContext(
                reason: AdminReauthenticationReasons.SessionStale,
                action: "admin.session.bootstrap",
                returnUrl,
                reauthenticationUrl,
                severity: AdminReauthenticationSeverities.Medium)));
    }

    var grantedPermissions = httpContext.User.FindAll(AdminClaimTypes.Permission)
        .Select(static claim => claim.Value)
        .Distinct(StringComparer.Ordinal)
        .ToHashSet(StringComparer.Ordinal);
    var capabilities = AdminSessionCapabilitiesBuilder.Build(grantedPermissions);

    var tokens = antiforgery.GetAndStoreTokens(httpContext);
    var permissions = grantedPermissions
        .OrderBy(static permission => permission, StringComparer.Ordinal)
        .ToArray();
    var privilegedSessionFreshUntilUtc = AdminSessionState.GetPrivilegedSessionFreshUntilUtc(httpContext.User, adminIdentityOptionsAccessor.Value);
    var requiresPrivilegedReauthentication = privilegedSessionFreshUntilUtc is null || privilegedSessionFreshUntilUtc <= timeProvider.GetUtcNow();

    return Results.Ok(new AdminSessionResponse(
        true,
        httpContext.User.FindFirstValue(System.Security.Claims.ClaimTypes.Name)
            ?? httpContext.User.FindFirstValue(OpenIddict.Abstractions.OpenIddictConstants.Claims.Name)
            ?? "unknown",
        httpContext.User.FindFirstValue(System.Security.Claims.ClaimTypes.Role)
            ?? httpContext.User.FindFirstValue(OpenIddict.Abstractions.OpenIddictConstants.Claims.Role)
            ?? "viewer",
        permissions,
        tokens.RequestToken ?? string.Empty,
        privilegedSessionFreshUntilUtc,
        requiresPrivilegedReauthentication,
        reauthenticationUrl,
        capabilities,
        AdminReauthentication.BuildContext(
            reason: requiresPrivilegedReauthentication ? AdminReauthenticationReasons.SessionStale : AdminReauthenticationReasons.PrivilegedAction,
            action: "admin.session.bootstrap",
            returnUrl,
            reauthenticationUrl,
            severity: requiresPrivilegedReauthentication
                ? AdminReauthentication.ResolveSeverity("admin.session.bootstrap", returnUrl)
                : AdminReauthenticationSeverities.Low)));
});

adminApi.MapGet("/session/heartbeat", async (HttpContext httpContext, Microsoft.Extensions.Options.IOptions<AdminIdentityOptions> adminIdentityOptionsAccessor, TimeProvider timeProvider, IRbacService rbacService) =>
{
    if (httpContext.User.Identity?.IsAuthenticated != true)
    {
        return Results.Ok(new AdminSessionHeartbeatResponse(
            false,
            null,
            false));
    }

    var sessionVersion = AdminSessionState.TryGetPermissionSnapshotVersion(httpContext.User);
    var currentVersion = await rbacService.GetPermissionSnapshotVersionAsync(httpContext.RequestAborted);
    if (sessionVersion is null || sessionVersion.Value != currentVersion)
    {
        return Results.Ok(new AdminSessionHeartbeatResponse(
            false,
            null,
            true));
    }

    var privilegedSessionFreshUntilUtc = AdminSessionState.GetPrivilegedSessionFreshUntilUtc(httpContext.User, adminIdentityOptionsAccessor.Value);
    var requiresPrivilegedReauthentication = privilegedSessionFreshUntilUtc is null || privilegedSessionFreshUntilUtc <= timeProvider.GetUtcNow();

    return Results.Ok(new AdminSessionHeartbeatResponse(
        true,
        privilegedSessionFreshUntilUtc,
        requiresPrivilegedReauthentication));
});

adminApi.MapGet("/session/capabilities", (HttpContext httpContext) =>
{
    var grantedPermissions = httpContext.User.FindAll(AdminClaimTypes.Permission)
        .Select(static claim => claim.Value)
        .Distinct(StringComparer.Ordinal)
        .ToHashSet(StringComparer.Ordinal);

    return Results.Ok(AdminSessionCapabilitiesBuilder.Build(grantedPermissions));
}).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.DashboardView));

adminApi.MapPost("/session/logout", async (HttpContext httpContext) =>
{
    await httpContext.SignOutAsync(Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme);
    return Results.NoContent();
}).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.DashboardView));

adminApi.MapGet("/cluster-overview", async ([FromServices] ISender sender, CancellationToken cancellationToken) =>
{
    var result = await sender.Send(new GetClusterOverviewQuery(), cancellationToken);
    return Results.Ok(result);
}).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.StatsView));

// ─── Cluster Shard Diagnostics ────────────────────────────────────────────────

adminApi.MapGet("/cluster/shards", async (
    [FromQuery] int totalShards,
    [FromServices] ISender sender,
    CancellationToken cancellationToken) =>
{
    var count = totalShards > 0 ? totalShards : 256;
    var result = await sender.Send(new GetClusterShardDiagnosticsQuery(count), cancellationToken);
    return Results.Ok(result);
}).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.NodesView));

adminApi.MapGet("/cluster/nodes", async (
    [FromServices] ISender sender,
    CancellationToken cancellationToken) =>
{
    var result = await sender.Send(new GetClusterNodeStatesQuery(), cancellationToken);
    return Results.Ok(result);
}).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.NodesView));

adminApi.MapGet("/nodes",
    async (string? search, string? sort, int? page, int? pageSize, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        var result = await sender.Send(
            new ListTrackerNodeConfigsQuery(GridQueryHttp.Bind(search, "all", sort, page, pageSize, AdminCatalogProfiles.TrackerNodes)),
            cancellationToken);
        return Results.Ok(result);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.SystemSettingsView));

adminApi.MapGet("/nodes/{nodeKey}/config",
    async (string nodeKey, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        var result = await sender.Send(new GetTrackerNodeConfigViewQuery(nodeKey), cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.SystemSettingsView));

adminApi.MapPost("/nodes/validate",
    async (TrackerNodeConfigurationDocument request, [FromServices] ITrackerNodeConfigurationReader reader, CancellationToken cancellationToken) =>
    {
        var result = await reader.ValidateTrackerNodeConfigurationAsync(request, cancellationToken);
        return Results.Ok(result);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.MaintenanceExecute));

adminApi.MapPut("/nodes/{nodeKey}/config",
    async (HttpContext httpContext, string nodeKey, TrackerNodeConfigurationUpsertRequest request, [FromServices] IAdminMutationOrchestrator orchestrator, CancellationToken cancellationToken) =>
    {
        return await AdminMutationEndpointExecutor.ExecuteAsync(
            async () => Results.Ok(await orchestrator.UpsertTrackerNodeConfigurationAsync(nodeKey, request, AdminAuthorization.CreateMutationContext(httpContext), cancellationToken)),
            httpContext);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.MaintenanceExecute));

adminApi.MapDelete("/nodes/{nodeKey}/config",
    async (HttpContext httpContext, string nodeKey, long? expectedVersion, [FromServices] IAdminMutationOrchestrator orchestrator, CancellationToken cancellationToken) =>
    {
        return await AdminMutationEndpointExecutor.ExecuteAsync(
            async () =>
            {
                await orchestrator.DeleteTrackerNodeConfigurationAsync(nodeKey, expectedVersion, AdminAuthorization.CreateMutationContext(httpContext), cancellationToken);
                return Results.NoContent();
            },
            httpContext);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.MaintenanceExecute));

adminApi.MapGet("/audit",
    async (string? search, string? filter, string? sort, int? page, int? pageSize, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        var result = await sender.Send(new ListAuditRecordsQuery(
        GridQueryHttp.Bind(search, filter, sort, page, pageSize, AdminCatalogProfiles.Audit),
            GridQueryHttp.ParseFilter<AuditRecordFilter>(filter)), cancellationToken);
        return Results.Ok(result);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.AuditView));

adminApi.MapGet("/maintenance",
    async (string? search, string? filter, string? sort, int? page, int? pageSize, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        var result = await sender.Send(new ListMaintenanceRunsQuery(
        GridQueryHttp.Bind(search, filter, sort, page, pageSize, AdminCatalogProfiles.Maintenance),
            GridQueryHttp.ParseFilter<MaintenanceRunFilter>(filter)), cancellationToken);
        return Results.Ok(result);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.SystemSettingsView));

adminApi.MapGet("/torrents",
    async (string? search, string? filter, string? sort, bool? isEnabled, bool? isPrivate, int? page, int? pageSize, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        var result = await sender.Send(new ListTorrentsQuery(
        GridQueryHttp.Bind(search, filter, sort, page, pageSize, AdminCatalogProfiles.Torrents),
            GridQueryHttp.ParseFilter<TorrentCatalogFilter>(filter),
            isEnabled,
            isPrivate), cancellationToken);
        return Results.Ok(result);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.TorrentsView));

adminApi.MapGet("/torrents/{infoHash}",
    async (string infoHash, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        var result = await sender.Send(new GetTorrentDetailQuery(infoHash), cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.TorrentsView));

adminApi.MapGet("/passkeys",
    async (string? search, string? filter, string? sort, Guid? userId, bool? isRevoked, int? page, int? pageSize, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        var result = await sender.Send(new ListPasskeysQuery(
        GridQueryHttp.Bind(search, filter, sort, page, pageSize, AdminCatalogProfiles.Passkeys),
            GridQueryHttp.ParseFilter<PasskeyCatalogFilter>(filter),
            userId,
            isRevoked), cancellationToken);
        return Results.Ok(result);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.PasskeysView));

adminApi.MapGet("/passkeys/{id:guid}",
    async (Guid id, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        var result = await sender.Send(new GetPasskeyDetailQuery(id), cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.PasskeysView));

adminApi.MapGet("/tracker-access",
    async (string? search, string? filter, string? sort, bool? canUsePrivateTracker, int? page, int? pageSize, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        var result = await sender.Send(new ListTrackerAccessRightsQuery(
        GridQueryHttp.Bind(search, filter, sort, page, pageSize, AdminCatalogProfiles.TrackerAccess),
            GridQueryHttp.ParseFilter<TrackerAccessRightsFilter>(filter),
            canUsePrivateTracker), cancellationToken);
        return Results.Ok(result);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.TrackerAccessView));

adminApi.MapGet("/tracker-access/{userId:guid}",
    async (Guid userId, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        var result = await sender.Send(new GetTrackerAccessRightQuery(userId), cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.TrackerAccessView));

adminApi.MapGet("/permissions",
    async (HttpContext httpContext, string? search, string? filter, string? sort, bool? canUsePrivateTracker, int? page, int? pageSize, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        DeprecatedApiAlias.Apply(httpContext, "/api/admin/tracker-access");
        var result = await sender.Send(new ListTrackerAccessRightsQuery(
        GridQueryHttp.Bind(search, filter, sort, page, pageSize, AdminCatalogProfiles.TrackerAccess),
            GridQueryHttp.ParseFilter<TrackerAccessRightsFilter>(filter),
            canUsePrivateTracker), cancellationToken);
        return Results.Ok(result);
    }).WithMetadata(new ObsoleteAttribute("Use /api/admin/tracker-access. This alias will be removed in a future release."))
    .AddEndpointFilter(new DeprecatedEndpointFilter("/api/admin/tracker-access"))
    .RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.TrackerAccessView));

adminApi.MapGet("/bans",
    async (string? search, string? filter, string? sort, string? scope, int? page, int? pageSize, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        var result = await sender.Send(new ListBansQuery(
        GridQueryHttp.Bind(search, filter, sort, page, pageSize, AdminCatalogProfiles.Bans),
            GridQueryHttp.ParseFilter<BanCatalogFilter>(filter),
            scope), cancellationToken);
        return Results.Ok(result);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.BansView));

adminApi.MapGet("/bans/{scope}/{subject}",
    async (string scope, string subject, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        var result = await sender.Send(new GetBanRuleQuery(scope, subject), cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.BansView));

adminApi.MapPost("/bans/{scope}/{subject}/expire",
    async (HttpContext httpContext, string scope, string subject, BanRuleExpireRequest request, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        return await AdminMutationEndpointExecutor.ExecuteAsync(
            async () => Results.Ok(await sender.Send(new ExpireBanAdminCommand(scope, subject, request, AdminAuthorization.CreateMutationContext(httpContext)), cancellationToken)),
            httpContext);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.BansManage));

adminApi.MapDelete("/bans/{scope}/{subject}",
    async (HttpContext httpContext, string scope, string subject, long? expectedVersion, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        return await AdminMutationEndpointExecutor.ExecuteAsync(
            async () =>
            {
                await sender.Send(new DeleteBanAdminCommand(scope, subject, expectedVersion, AdminAuthorization.CreateMutationContext(httpContext)), cancellationToken);
                return Results.NoContent();
            },
            httpContext);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.BansManage));

adminApi.MapPut("/torrents/{infoHash}/policy",
    async (HttpContext httpContext, string infoHash, TorrentPolicyUpsertRequest request, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        return await AdminMutationEndpointExecutor.ExecuteAsync(
            async () => Results.Ok(await sender.Send(new UpsertTorrentPolicyAdminCommand(infoHash, request, AdminAuthorization.CreateMutationContext(httpContext)), cancellationToken)),
            httpContext);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.TrackerPoliciesEdit));

adminApi.MapDelete("/torrents/{infoHash}",
    async (HttpContext httpContext, string infoHash, long? expectedVersion, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        return await AdminMutationEndpointExecutor.ExecuteAsync(
            async () =>
            {
                await sender.Send(new DeleteTorrentAdminCommand(infoHash, expectedVersion, AdminAuthorization.CreateMutationContext(httpContext)), cancellationToken);
                return Results.NoContent();
            },
            httpContext);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.TorrentsEdit));

adminApi.MapPost("/torrents/bulk/activate",
    async (HttpContext httpContext, BulkTorrentActivationRequest request, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        var validationError = AdminBulkRequestValidator.Validate(request.Items.Count, 200, "torrents");
        if (validationError is not null)
        {
            return validationError;
        }

        return await AdminMutationEndpointExecutor.ExecuteAsync(
            async () => Results.Ok(await sender.Send(new BulkActivateTorrentsAdminCommand(request.Items, AdminAuthorization.CreateMutationContext(httpContext)), cancellationToken)),
            httpContext);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.TorrentsEdit));

adminApi.MapPost("/torrents/bulk/deactivate",
    async (HttpContext httpContext, BulkTorrentActivationRequest request, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        var validationError = AdminBulkRequestValidator.Validate(request.Items.Count, 200, "torrents");
        if (validationError is not null)
        {
            return validationError;
        }

        return await AdminMutationEndpointExecutor.ExecuteAsync(
            async () => Results.Ok(await sender.Send(new BulkDeactivateTorrentsAdminCommand(request.Items, AdminAuthorization.CreateMutationContext(httpContext)), cancellationToken)),
            httpContext);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.TorrentsEdit));

adminApi.MapPut("/torrents/bulk/policy",
    async (HttpContext httpContext, BulkTorrentPolicyUpsertRequest request, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        var validationError = AdminBulkRequestValidator.Validate(request.Items.Count, 50, "torrent policies");
        if (validationError is not null)
        {
            return validationError;
        }

        return await AdminMutationEndpointExecutor.ExecuteAsync(
            async () => Results.Ok(await sender.Send(new BulkUpsertTorrentPoliciesAdminCommand(request.Items, AdminAuthorization.CreateMutationContext(httpContext)), cancellationToken)),
            httpContext);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.TrackerPoliciesEdit));

adminApi.MapPost("/torrents/bulk/policy/dry-run",
    async (BulkTorrentPolicyUpsertRequest request, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        var validationError = AdminBulkRequestValidator.Validate(request.Items.Count, 50, "torrent policies");
        if (validationError is not null)
        {
            return validationError;
        }

        return Results.Ok(await sender.Send(new DryRunBulkUpsertTorrentPoliciesAdminCommand(request.Items), cancellationToken));
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.TrackerPoliciesEdit));

adminApi.MapPost("/passkeys",
    async (HttpContext httpContext, CreatePasskeyAdminRequest request, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        return await AdminMutationEndpointExecutor.ExecuteAsync(
            async () => Results.Ok(await sender.Send(new CreatePasskeyAdminCommand(
                new PasskeyCreateRequest(request.Passkey, request.UserId, request.IsRevoked, request.ExpiresAtUtc, request.ExpectedVersion),
                AdminAuthorization.CreateMutationContext(httpContext)), cancellationToken)),
            httpContext);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.PasskeysManage));

adminApi.MapPut("/passkeys/{passkey}",
    async (HttpContext httpContext, string passkey, PasskeyUpsertRequest request, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        return await AdminMutationEndpointExecutor.ExecuteAsync(
            async () => Results.Ok(await sender.Send(new UpsertPasskeyAdminCommand(passkey, request, AdminAuthorization.CreateMutationContext(httpContext)), cancellationToken)),
            httpContext);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.PasskeysManage));

adminApi.MapPut("/passkeys/id/{id:guid}",
    async (HttpContext httpContext, Guid id, PasskeyUpsertRequest request, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        return await AdminMutationEndpointExecutor.ExecuteAsync(
            async () => Results.Ok(await sender.Send(new UpsertPasskeyByIdAdminCommand(id, request, AdminAuthorization.CreateMutationContext(httpContext)), cancellationToken)),
            httpContext);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.PasskeysManage));

adminApi.MapPost("/passkeys/id/{id:guid}/revoke",
    async (HttpContext httpContext, Guid id, PasskeyRevokeRequest request, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        return await AdminMutationEndpointExecutor.ExecuteAsync(
            async () => Results.Ok(await sender.Send(new RevokePasskeyByIdAdminCommand(id, request, AdminAuthorization.CreateMutationContext(httpContext)), cancellationToken)),
            httpContext);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.PasskeysManage));

adminApi.MapPost("/passkeys/id/{id:guid}/rotate",
    async (HttpContext httpContext, Guid id, PasskeyRotateRequest request, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        return await AdminMutationEndpointExecutor.ExecuteAsync(
            async () => Results.Ok(await sender.Send(new RotatePasskeyByIdAdminCommand(id, request, AdminAuthorization.CreateMutationContext(httpContext)), cancellationToken)),
            httpContext);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.PasskeysManage));

adminApi.MapDelete("/passkeys/id/{id:guid}",
    async (HttpContext httpContext, Guid id, long? expectedVersion, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        return await AdminMutationEndpointExecutor.ExecuteAsync(
            async () =>
            {
                await sender.Send(new DeletePasskeyAdminCommand(id, expectedVersion, AdminAuthorization.CreateMutationContext(httpContext)), cancellationToken);
                return Results.NoContent();
            },
            httpContext);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.PasskeysManage));

adminApi.MapPost("/passkeys/bulk/revoke",
    async (HttpContext httpContext, BulkPasskeyRevokeRequest request, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        var validationError = AdminBulkRequestValidator.Validate(request.Items.Count, 200, "passkeys");
        if (validationError is not null)
        {
            return validationError;
        }

        return await AdminMutationEndpointExecutor.ExecuteAsync(
            async () => Results.Ok(await sender.Send(new BulkRevokePasskeysAdminCommand(request.Items, AdminAuthorization.CreateMutationContext(httpContext)), cancellationToken)),
            httpContext);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.PasskeysManage));

adminApi.MapPost("/passkeys/bulk/rotate",
    async (HttpContext httpContext, BulkPasskeyRotateRequest request, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        var validationError = AdminBulkRequestValidator.Validate(request.Items.Count, 100, "passkeys");
        if (validationError is not null)
        {
            return validationError;
        }

        return await AdminMutationEndpointExecutor.ExecuteAsync(
            async () => Results.Ok(await sender.Send(new BulkRotatePasskeysAdminCommand(request.Items, AdminAuthorization.CreateMutationContext(httpContext)), cancellationToken)),
            httpContext);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.PasskeysManage));

adminApi.MapPut("/users/{userId:guid}/tracker-access",
    async (HttpContext httpContext, Guid userId, TrackerAccessRightsUpsertRequest request, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        return await AdminMutationEndpointExecutor.ExecuteAsync(
            async () => Results.Ok(await sender.Send(new UpsertTrackerAccessRightsAdminCommand(userId, request, AdminAuthorization.CreateMutationContext(httpContext)), cancellationToken)),
            httpContext);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.TrackerAccessManage));

adminApi.MapDelete("/users/{userId:guid}/tracker-access",
    async (HttpContext httpContext, Guid userId, long? expectedVersion, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        return await AdminMutationEndpointExecutor.ExecuteAsync(
            async () =>
            {
                await sender.Send(new DeleteTrackerAccessRightsAdminCommand(userId, expectedVersion, AdminAuthorization.CreateMutationContext(httpContext)), cancellationToken);
                return Results.NoContent();
            },
            httpContext);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.TrackerAccessManage));

#pragma warning disable CS0618
adminApi.MapPut("/users/{userId:guid}/permissions",
    async (HttpContext httpContext, Guid userId, UserPermissionUpsertRequest request, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        DeprecatedApiAlias.Apply(httpContext, $"/api/admin/users/{userId:guid}/tracker-access");
        return await AdminMutationEndpointExecutor.ExecuteAsync(
            async () => Results.Ok(await sender.Send(new UpsertTrackerAccessRightsAdminCommand(userId, request.ToTrackerAccessRightsRequest(), AdminAuthorization.CreateMutationContext(httpContext)), cancellationToken)),
            httpContext);
    }).WithMetadata(new ObsoleteAttribute("Use /api/admin/users/{userId}/tracker-access. This alias will be removed in a future release."))
    .AddEndpointFilter(new DeprecatedEndpointFilter("/api/admin/users/{userId}/tracker-access"))
    .RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.TrackerAccessManage));

adminApi.MapPut("/users/bulk/tracker-access",
    async (HttpContext httpContext, BulkTrackerAccessRightsUpsertRequest request, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        var validationError = AdminBulkRequestValidator.Validate(request.Items.Count, 200, "tracker access");
        if (validationError is not null)
        {
            return validationError;
        }

        return await AdminMutationEndpointExecutor.ExecuteAsync(
            async () => Results.Ok(await sender.Send(new BulkUpsertTrackerAccessRightsAdminCommand(request.Items, AdminAuthorization.CreateMutationContext(httpContext)), cancellationToken)),
            httpContext);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.TrackerAccessManage));

adminApi.MapPut("/users/bulk/permissions",
    async (HttpContext httpContext, BulkUserPermissionUpsertRequest request, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        DeprecatedApiAlias.Apply(httpContext, "/api/admin/users/bulk/tracker-access");
        var validationError = AdminBulkRequestValidator.Validate(request.Items.Count, 200, "permissions");
        if (validationError is not null)
        {
            return validationError;
        }

        return await AdminMutationEndpointExecutor.ExecuteAsync(
            async () => Results.Ok(await sender.Send(new BulkUpsertTrackerAccessRightsAdminCommand(request.Items.Select(static item => item.ToTrackerAccessRightsItem()).ToArray(), AdminAuthorization.CreateMutationContext(httpContext)), cancellationToken)),
            httpContext);
    }).WithMetadata(new ObsoleteAttribute("Use /api/admin/users/bulk/tracker-access. This alias will be removed in a future release."))
    .AddEndpointFilter(new DeprecatedEndpointFilter("/api/admin/users/bulk/tracker-access"))
    .RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.TrackerAccessManage));
#pragma warning restore CS0618

adminApi.MapPut("/bans/{scope}/{subject}",
    async (HttpContext httpContext, string scope, string subject, BanRuleUpsertRequest request, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        return await AdminMutationEndpointExecutor.ExecuteAsync(
            async () => Results.Ok(await sender.Send(new UpsertBanAdminCommand(scope, subject, request, AdminAuthorization.CreateMutationContext(httpContext)), cancellationToken)),
            httpContext);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.BansManage));

adminApi.MapPut("/bans/bulk",
    async (HttpContext httpContext, BulkBanRuleUpsertRequest request, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        var validationError = AdminBulkRequestValidator.Validate(request.Items.Count, 200, "bans");
        if (validationError is not null)
        {
            return validationError;
        }

        return await AdminMutationEndpointExecutor.ExecuteAsync(
            async () => Results.Ok(await sender.Send(new BulkUpsertBansAdminCommand(request.Items, AdminAuthorization.CreateMutationContext(httpContext)), cancellationToken)),
            httpContext);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.BansManage));

adminApi.MapPost("/bans/bulk/expire",
    async (HttpContext httpContext, BulkBanRuleExpireRequest request, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        var validationError = AdminBulkRequestValidator.Validate(request.Items.Count, 200, "bans");
        if (validationError is not null)
        {
            return validationError;
        }

        return await AdminMutationEndpointExecutor.ExecuteAsync(
            async () => Results.Ok(await sender.Send(new BulkExpireBansAdminCommand(request.Items, AdminAuthorization.CreateMutationContext(httpContext)), cancellationToken)),
            httpContext);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.BansManage));

adminApi.MapPost("/bans/bulk/delete",
    async (HttpContext httpContext, BulkBanRuleDeleteRequest request, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        var validationError = AdminBulkRequestValidator.Validate(request.Items.Count, 200, "bans");
        if (validationError is not null)
        {
            return validationError;
        }

        return await AdminMutationEndpointExecutor.ExecuteAsync(
            async () => Results.Ok(await sender.Send(new BulkDeleteBansAdminCommand(request.Items, AdminAuthorization.CreateMutationContext(httpContext)), cancellationToken)),
            httpContext);
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.BansManage));
adminApi.MapPost("/maintenance/cache-refresh",
    async (HttpContext httpContext, [FromServices] ISender sender, CancellationToken cancellationToken) =>
    {
        await sender.Send(new TriggerCacheRefreshAdminCommand("cache-refresh", AdminAuthorization.CreateMutationContext(httpContext)), cancellationToken);
        return Results.Accepted();
    }).RequireAuthorization(AdminAuthorizationPolicies.ForPermission(AdminPermissions.MaintenanceExecute));

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapFallback((HttpContext httpContext) =>
{
    if (httpContext.Request.Path.StartsWithSegments("/api", StringComparison.Ordinal) ||
        httpContext.Request.Path.StartsWithSegments("/account", StringComparison.Ordinal) ||
        httpContext.Request.Path.StartsWithSegments("/connect", StringComparison.Ordinal) ||
        httpContext.Request.Path.StartsWithSegments("/health", StringComparison.Ordinal) ||
        httpContext.Request.Path.StartsWithSegments("/admin-ui/config", StringComparison.Ordinal))
    {
        return Results.NotFound();
    }

    return Results.File(Path.Combine(app.Environment.WebRootPath ?? string.Empty, "index.html"), "text/html; charset=utf-8");
});

app.Run();

sealed class AdminStartupService(IServiceProvider serviceProvider, IReadinessState readinessState) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await StartupBootstrap.WaitForPostgresAsync(serviceProvider, "admin-service", cancellationToken);
        await StartupBootstrap.WaitForRedisAsync(serviceProvider, "admin-service", cancellationToken);

        using var scope = serviceProvider.CreateScope();
        await StartupBootstrap.MigrateDbContextAsync<AdminIdentityDbContext>(scope.ServiceProvider, cancellationToken);
        await StartupBootstrap.MigrateDbContextAsync<Audit.Infrastructure.AuditDbContext>(scope.ServiceProvider, cancellationToken);
        await StartupBootstrap.MigrateDbContextAsync<Notification.Infrastructure.NotificationDbContext>(scope.ServiceProvider, cancellationToken);
        await StartupBootstrap.MigrateDbContextAsync<Identity.SelfService.Infrastructure.SelfServiceDbContext>(scope.ServiceProvider, cancellationToken);
        await scope.ServiceProvider.GetRequiredService<Identity.SelfService.Infrastructure.RbacSeedService>().SeedAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<AdminIdentitySeedService>().SeedAsync(cancellationToken);
        readinessState.MarkReady();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public partial class Program;

sealed record AdminSessionResponse(
    bool IsAuthenticated,
    string UserName,
    string Role,
    IReadOnlyList<string> Permissions,
    string CsrfToken,
    DateTimeOffset? PrivilegedSessionFreshUntilUtc,
    bool RequiresPrivilegedReauthentication,
    string ReauthenticationUrl,
    IReadOnlyList<AdminSessionCapability> Capabilities,
    AdminReauthenticationContext ReauthenticationContext);

sealed record BulkPasskeyRevokeRequest(IReadOnlyCollection<BulkPasskeyRevokeItem> Items);

sealed record BulkPasskeyRotateRequest(IReadOnlyCollection<BulkPasskeyRotateItem> Items);

sealed record CreatePasskeyAdminRequest(
    string Passkey,
    Guid UserId,
    bool IsRevoked,
    DateTimeOffset? ExpiresAtUtc,
    long? ExpectedVersion = null);

sealed record BulkTorrentActivationRequest(IReadOnlyCollection<BulkTorrentActivationItem> Items);

sealed record BulkTorrentPolicyUpsertRequest(IReadOnlyCollection<BulkTorrentPolicyUpsertItem> Items);

#pragma warning disable CS0618
sealed record BulkUserPermissionUpsertRequest(IReadOnlyCollection<BulkUserPermissionUpsertItem> Items);
#pragma warning restore CS0618
sealed record BulkTrackerAccessRightsUpsertRequest(IReadOnlyCollection<BulkTrackerAccessRightsUpsertItem> Items);

sealed record BulkBanRuleUpsertRequest(IReadOnlyCollection<BulkBanRuleUpsertItem> Items);

sealed record BulkBanRuleExpireRequest(IReadOnlyCollection<BulkBanRuleExpireItem> Items);

sealed record BulkBanRuleDeleteRequest(IReadOnlyCollection<BulkBanRuleDeleteItem> Items);

sealed record AdminSessionCapability(
    string Action,
    string Permission,
    string HttpMethod,
    bool SupportsBulk,
    string? BulkRoutePattern,
    string? DryRunRoutePattern,
    string SelectionMode,
    string IdempotencyHint,
    bool ConfirmationRequired,
    string ConfirmationSeverity,
    bool DryRunSupported,
    bool Granted,
    bool RequiresPrivilegedReauthentication,
    string Severity,
    string Category,
    string ResourceKind,
    string RoutePattern,
    string DisplayName,
    string ReauthenticationPrompt,
    string PayloadKind,
    int? MaxItems,
    IReadOnlyList<string> SupportedFields,
    IReadOnlyList<string> RequiredFields,
    IReadOnlyDictionary<string, string> FieldTypes,
    string ResponseKind,
    string? ResultItemKind,
    string? ResultCollectionProperty);

sealed record AdminReauthenticationContext(
    string Reason,
    string Action,
    string ReturnUrl,
    string ReauthenticationUrl,
    string Severity);

sealed record AdminSessionHeartbeatResponse(
    bool IsAuthenticated,
    DateTimeOffset? PrivilegedSessionFreshUntilUtc,
    bool RequiresPrivilegedReauthentication);

sealed record AdminUiClientConfigurationResponse(
    string Authority,
    string ClientId,
    string RedirectUri,
    string PostLogoutRedirectUri,
    string Scope,
    string ResponseType);

static class AdminMutationEndpointExecutor
{
    public static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action, HttpContext httpContext)
    {
        try
        {
            return await action();
        }
        catch (ConfigurationConcurrencyException exception)
        {
            return Results.Conflict(new
            {
                code = "concurrency_conflict",
                message = exception.Message,
                entityType = exception.EntityType,
                entityKey = exception.EntityKey,
                expectedVersion = exception.ExpectedVersion,
                actualVersion = exception.ActualVersion,
                correlationId = AdminAuthorization.CreateMutationContext(httpContext).CorrelationId
            });
        }
        catch (ConfigurationEntityNotFoundException exception)
        {
            return Results.NotFound(new
            {
                code = "not_found",
                message = exception.Message,
                entityType = exception.EntityType,
                entityKey = exception.EntityKey,
                correlationId = AdminAuthorization.CreateMutationContext(httpContext).CorrelationId
            });
        }
        catch (ValidationException exception)
        {
            return Results.BadRequest(new
            {
                code = "validation_failed",
                message = exception.Message,
                correlationId = AdminAuthorization.CreateMutationContext(httpContext).CorrelationId
            });
        }
    }
}

static class AdminBulkRequestValidator
{
    public static IResult? Validate(int count, int maxCount, string resourceName)
    {
        if (count <= 0)
        {
            return Results.BadRequest(new
            {
                code = "invalid_bulk_request",
                message = $"At least one {resourceName} item must be supplied."
            });
        }

        if (count > maxCount)
        {
            return Results.BadRequest(new
            {
                code = "bulk_limit_exceeded",
                message = $"A maximum of {maxCount} {resourceName} items can be processed in a single request."
            });
        }

        return null;
    }
}

sealed class DeprecatedEndpointFilter(string canonicalPath) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        DeprecatedApiAlias.Apply(context.HttpContext, canonicalPath);
        return await next(context);
    }
}

static class DeprecatedApiAlias
{
    private const string RemovalDate = "Wed, 30 Sep 2026 00:00:00 GMT";

    public static void Apply(HttpContext httpContext, string canonicalPath)
    {
        TrackerDiagnostics.AdminLegacyTrackerAccessAliasHit.Add(
            1,
            new KeyValuePair<string, object?>("legacy_route", httpContext.Request.Path.Value ?? string.Empty),
            new KeyValuePair<string, object?>("canonical_route", canonicalPath),
            new KeyValuePair<string, object?>("method", httpContext.Request.Method));
        TrackerDiagnostics.CompatibilityWarningIssued.Add(
            1,
            new KeyValuePair<string, object?>("category", "admin-tracker-access-alias"),
            new KeyValuePair<string, object?>("legacy_route", httpContext.Request.Path.Value ?? string.Empty));

        var logger = httpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("BeeTracker.Admin.DeprecatedApiAlias");
        logger.LogWarning(
            "Deprecated admin tracker-access alias invoked. LegacyRoute={LegacyRoute} CanonicalRoute={CanonicalRoute} Method={Method}",
            httpContext.Request.Path.Value ?? string.Empty,
            canonicalPath,
            httpContext.Request.Method);

        httpContext.Response.Headers["Deprecation"] = "true";
        httpContext.Response.Headers["Sunset"] = RemovalDate;
        httpContext.Response.Headers["Link"] = $"<{canonicalPath}>; rel=\"successor-version\"";
    }
}

static class AdminSessionCapabilitiesBuilder
{
    public static AdminSessionCapability[] Build(IReadOnlySet<string> grantedPermissions)
        => AdminCapabilities.All
            .Select(static descriptor => descriptor)
            .Select(descriptor => new AdminSessionCapability(
                descriptor.Action,
                descriptor.Permission,
                descriptor.HttpMethod,
                descriptor.SupportsBulk,
                descriptor.BulkRoutePattern,
                descriptor.DryRunRoutePattern,
                descriptor.SelectionMode,
                descriptor.IdempotencyHint,
                descriptor.ConfirmationRequired,
                descriptor.ConfirmationSeverity,
                descriptor.DryRunSupported,
                grantedPermissions.Contains(descriptor.Permission),
                descriptor.RequiresPrivilegedReauthentication,
                descriptor.Severity,
                descriptor.Category,
                descriptor.ResourceKind,
                descriptor.RoutePattern,
                descriptor.DisplayName,
                descriptor.ReauthenticationPrompt,
                descriptor.PayloadKind,
                descriptor.MaxItems,
                descriptor.SupportedFields,
                descriptor.RequiredFields,
                descriptor.FieldTypes,
                descriptor.ResponseKind,
                descriptor.ResultItemKind,
                descriptor.ResultCollectionProperty))
        .OrderBy(static capability => capability.Action, StringComparer.Ordinal)
        .ToArray();
}
