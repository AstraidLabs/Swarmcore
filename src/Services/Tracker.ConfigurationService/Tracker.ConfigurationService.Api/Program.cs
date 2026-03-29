using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using BeeTracker.BuildingBlocks.Abstractions.Hosting;
using BeeTracker.BuildingBlocks.Observability.Diagnostics;
using BeeTracker.Contracts.Configuration;
using BeeTracker.Hosting;
using Tracker.ConfigurationService.Application;
using Tracker.ConfigurationService.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddBeeTrackerInfrastructure(builder.Configuration, usePostgres: true, useRedis: true);
builder.Services.AddConfigurationInfrastructure(builder.Configuration);
builder.Services.AddHostedService<ConfigurationStartupService>();

var app = builder.Build();

app.MapCommonHealthEndpoints();

app.MapGet("/api/configuration/torrents", async (ITorrentConfigurationReader reader, CancellationToken cancellationToken) =>
{
    var result = await reader.GetTorrentPoliciesAsync(cancellationToken);
    return Results.Ok(result);
});

app.MapGet("/api/configuration/nodes/{nodeKey}",
    async (string nodeKey, [FromServices] ITrackerNodeConfigurationReader reader, CancellationToken cancellationToken) =>
    {
        var result = await reader.GetTrackerNodeConfigurationAsync(nodeKey, cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(result);
    });

app.MapGet("/api/configuration/nodes/{nodeKey}/effective",
    async (string nodeKey, [FromServices] ITrackerNodeConfigurationReader reader, CancellationToken cancellationToken) =>
    {
        var result = await reader.GetEffectiveTrackerNodeConfigurationAsync(nodeKey, cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(result);
    });

app.MapPost("/api/configuration/nodes/validate",
    async (TrackerNodeConfigurationDocument request, [FromServices] ITrackerNodeConfigurationReader reader, CancellationToken cancellationToken) =>
    {
        var result = await reader.ValidateTrackerNodeConfigurationAsync(request, cancellationToken);
        return Results.Ok(result);
    });

app.MapPut("/api/configuration/nodes/{nodeKey}",
    async (HttpContext httpContext, string nodeKey, TrackerNodeConfigurationUpsertRequest request, [FromServices] IConfigurationMutationService mutationService, CancellationToken cancellationToken) =>
    {
        return await MutationEndpointExecutor.ExecuteAsync(
            async () => Results.Ok(await mutationService.UpsertTrackerNodeConfigurationAsync(nodeKey, request, MutationContextFactory.Create(httpContext), cancellationToken)),
            httpContext);
    });

app.MapPut("/api/configuration/torrents/{infoHash}/policy",
    async (HttpContext httpContext, string infoHash, TorrentPolicyUpsertRequest request, [FromServices] IConfigurationMutationService mutationService, CancellationToken cancellationToken) =>
    {
        return await MutationEndpointExecutor.ExecuteAsync(
            async () => Results.Ok(await mutationService.UpsertTorrentPolicyAsync(infoHash, request, MutationContextFactory.Create(httpContext), cancellationToken)),
            httpContext);
    });

app.MapPut("/api/configuration/passkeys/{passkey}",
    async (HttpContext httpContext, string passkey, PasskeyUpsertRequest request, [FromServices] IConfigurationMutationService mutationService, CancellationToken cancellationToken) =>
    {
        return await MutationEndpointExecutor.ExecuteAsync(
            async () => Results.Ok(await mutationService.UpsertPasskeyAsync(passkey, request, MutationContextFactory.Create(httpContext), cancellationToken)),
            httpContext);
    });

app.MapPut("/api/configuration/users/{userId:guid}/tracker-access",
    async (HttpContext httpContext, Guid userId, TrackerAccessRightsUpsertRequest request, [FromServices] IConfigurationMutationService mutationService, CancellationToken cancellationToken) =>
    {
        return await MutationEndpointExecutor.ExecuteAsync(
            async () => Results.Ok(await mutationService.UpsertTrackerAccessRightsAsync(userId, request, MutationContextFactory.Create(httpContext), cancellationToken)),
            httpContext);
    });

#pragma warning disable CS0618
app.MapPut("/api/configuration/users/{userId:guid}/permissions",
    async (HttpContext httpContext, Guid userId, UserPermissionUpsertRequest request, [FromServices] IConfigurationMutationService mutationService, CancellationToken cancellationToken) =>
    {
        DeprecatedConfigurationAlias.Apply(httpContext, $"/api/configuration/users/{userId}/tracker-access");
        return await MutationEndpointExecutor.ExecuteAsync(
            async () => Results.Ok(await mutationService.UpsertTrackerAccessRightsAsync(userId, request.ToTrackerAccessRightsRequest(), MutationContextFactory.Create(httpContext), cancellationToken)),
            httpContext);
    }).WithMetadata(new ObsoleteAttribute("Use /api/configuration/users/{userId}/tracker-access. This alias will be removed in a future release."))
    .AddEndpointFilter(new DeprecatedConfigurationEndpointFilter("/api/configuration/users/{userId}/tracker-access"));
#pragma warning restore CS0618

app.MapPut("/api/configuration/bans/{scope}/{subject}",
    async (HttpContext httpContext, string scope, string subject, BanRuleUpsertRequest request, [FromServices] IConfigurationMutationService mutationService, CancellationToken cancellationToken) =>
    {
        return await MutationEndpointExecutor.ExecuteAsync(
            async () => Results.Ok(await mutationService.UpsertBanRuleAsync(scope, subject, request, MutationContextFactory.Create(httpContext), cancellationToken)),
            httpContext);
    });

app.MapDelete("/api/configuration/bans/{scope}/{subject}",
    async (HttpContext httpContext, string scope, string subject, long? expectedVersion, [FromServices] IConfigurationMutationService mutationService, CancellationToken cancellationToken) =>
    {
        return await MutationEndpointExecutor.ExecuteAsync(
            async () =>
            {
                await mutationService.DeleteBanRuleAsync(scope, subject, expectedVersion, MutationContextFactory.Create(httpContext), cancellationToken);
                return Results.NoContent();
            },
            httpContext);
    });

app.MapPost("/api/configuration/maintenance/cache-refresh",
    async (HttpContext httpContext, [FromServices] IConfigurationMaintenanceService maintenanceService, CancellationToken cancellationToken) =>
    {
        await maintenanceService.TriggerCacheRefreshAsync("cache-refresh", MutationContextFactory.Create(httpContext), cancellationToken);
        return Results.Accepted();
    });

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Run();

sealed class ConfigurationStartupService(IServiceProvider serviceProvider, IReadinessState readinessState) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await StartupBootstrap.WaitForPostgresAsync(serviceProvider, "configuration-service", cancellationToken);
        await StartupBootstrap.WaitForRedisAsync(serviceProvider, "configuration-service", cancellationToken);

        using var scope = serviceProvider.CreateScope();
        await StartupBootstrap.MigrateDbContextAsync<TrackerConfigurationDbContext>(scope.ServiceProvider, cancellationToken);
        readinessState.MarkReady();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public partial class Program;

static class MutationContextFactory
{
    public static AdminMutationContext Create(HttpContext httpContext)
    {
        var actorId = httpContext.Request.Headers["X-Admin-Actor"].ToString();
        var correlationId = httpContext.Request.Headers["X-Correlation-Id"].ToString();

        return new AdminMutationContext(
            string.IsNullOrWhiteSpace(actorId) ? "system" : actorId,
            httpContext.Request.Headers["X-Admin-Role"].ToString() is { Length: > 0 } role ? role : "system",
            string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId,
            httpContext.TraceIdentifier,
            httpContext.Connection.RemoteIpAddress?.ToString(),
            httpContext.Request.Headers.UserAgent.ToString());
    }
}

sealed class DeprecatedConfigurationEndpointFilter(string canonicalPath) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        DeprecatedConfigurationAlias.Apply(context.HttpContext, canonicalPath);
        return await next(context);
    }
}

static class DeprecatedConfigurationAlias
{
    private const string RemovalDate = "Wed, 30 Sep 2026 00:00:00 GMT";

    public static void Apply(HttpContext httpContext, string canonicalPath)
    {
        TrackerDiagnostics.ConfigurationLegacyTrackerAccessAliasHit.Add(
            1,
            new KeyValuePair<string, object?>("legacy_route", httpContext.Request.Path.Value ?? string.Empty),
            new KeyValuePair<string, object?>("canonical_route", canonicalPath),
            new KeyValuePair<string, object?>("method", httpContext.Request.Method));
        TrackerDiagnostics.CompatibilityWarningIssued.Add(
            1,
            new KeyValuePair<string, object?>("category", "configuration-tracker-access-alias"),
            new KeyValuePair<string, object?>("legacy_route", httpContext.Request.Path.Value ?? string.Empty));

        var logger = httpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("BeeTracker.Configuration.DeprecatedApiAlias");
        logger.LogWarning(
            "Deprecated configuration tracker-access alias invoked. LegacyRoute={LegacyRoute} CanonicalRoute={CanonicalRoute} Method={Method}",
            httpContext.Request.Path.Value ?? string.Empty,
            canonicalPath,
            httpContext.Request.Method);

        httpContext.Response.Headers["Deprecation"] = "true";
        httpContext.Response.Headers["Sunset"] = RemovalDate;
        httpContext.Response.Headers["Link"] = $"<{canonicalPath}>; rel=\"successor-version\"";
    }
}

static class MutationEndpointExecutor
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
                correlationId = MutationContextFactory.Create(httpContext).CorrelationId
            });
        }
        catch (ValidationException exception)
        {
            return Results.BadRequest(new
            {
                code = "validation_failed",
                message = exception.Message,
                correlationId = MutationContextFactory.Create(httpContext).CorrelationId
            });
        }
    }
}
