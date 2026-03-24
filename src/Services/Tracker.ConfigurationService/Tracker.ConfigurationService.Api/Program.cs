using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Mvc;
using BeeTracker.BuildingBlocks.Abstractions.Hosting;
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

app.MapPut("/api/configuration/users/{userId:guid}/permissions",
    async (HttpContext httpContext, Guid userId, UserPermissionUpsertRequest request, [FromServices] IConfigurationMutationService mutationService, CancellationToken cancellationToken) =>
    {
        return await MutationEndpointExecutor.ExecuteAsync(
            async () => Results.Ok(await mutationService.UpsertUserPermissionsAsync(userId, request, MutationContextFactory.Create(httpContext), cancellationToken)),
            httpContext);
    });

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
    }
}
