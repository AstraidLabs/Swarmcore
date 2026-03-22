using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Swarmcore.BuildingBlocks.Abstractions.Hosting;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using Swarmcore.BuildingBlocks.Observability.Diagnostics;
using Swarmcore.Contracts.Telemetry;
using Swarmcore.Hosting;
using Tracker.TelemetryIngest.Application;
using Tracker.TelemetryIngest.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSwarmcoreInfrastructure(builder.Configuration, usePostgres: true, useRedis: false);
builder.Services.AddTelemetryInfrastructure();
builder.Services.AddHostedService<TelemetryStartupService>();
builder.Services.AddHostedService<TelemetryIngestWorker>();

var host = builder.Build();
await host.RunAsync();

sealed class TelemetryStartupService(IServiceProvider serviceProvider, IReadinessState readinessState) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await StartupBootstrap.WaitForPostgresAsync(serviceProvider, "telemetry-ingest", cancellationToken);

        using var scope = serviceProvider.CreateScope();
        await StartupBootstrap.MigrateDbContextAsync<TelemetryDbContext>(scope.ServiceProvider, cancellationToken);
        readinessState.MarkReady();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

sealed class TelemetryIngestWorker(
    ITelemetryBuffer telemetryBuffer,
    ITelemetryBatchSink telemetryBatchSink,
    ILogger<TelemetryIngestWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var batch in telemetryBuffer.ReadBatchesAsync(stoppingToken))
        {
            try
            {
                await telemetryBatchSink.PersistBatchAsync(batch, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                TrackerDiagnostics.TelemetryDropped.Add(batch.Count);
                logger.LogError(ex, "Failed to persist telemetry batch of {Count} records.", batch.Count);
            }
        }
    }
}
