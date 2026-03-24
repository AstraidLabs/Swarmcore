using System.Threading.Channels;
using Audit.Application;
using Audit.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BeeTracker.BuildingBlocks.Abstractions.Options;

namespace Audit.Infrastructure;

internal sealed class EfAuditWriter(AuditDbContext dbContext) : IAuditWriter
{
    public async Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        var entity = new AuditRecordEntity
        {
            Id = record.Id,
            Action = record.Action,
            ActorId = record.ActorId,
            TargetUserId = record.TargetUserId,
            CorrelationId = record.CorrelationId,
            IpAddress = record.IpAddress,
            UserAgent = record.UserAgent,
            Outcome = record.Outcome,
            ReasonCode = record.ReasonCode,
            MetadataJson = record.MetadataJson,
            OccurredAtUtc = record.OccurredAtUtc,
        };

        dbContext.AuditRecords.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class EfAuditReader(AuditDbContext dbContext) : IAuditReader
{
    public async Task<IReadOnlyList<AuditRecordDto>> GetRecentAsync(
        int count, int offset, CancellationToken cancellationToken = default)
    {
        var entities = await dbContext.AuditRecords
            .AsNoTracking()
            .OrderByDescending(static e => e.OccurredAtUtc)
            .Skip(offset)
            .Take(count)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToDto).ToList();
    }

    public async Task<IReadOnlyList<AuditRecordDto>> GetByActorAsync(
        string actorId, int count, CancellationToken cancellationToken = default)
    {
        var entities = await dbContext.AuditRecords
            .AsNoTracking()
            .Where(e => e.ActorId == actorId)
            .OrderByDescending(static e => e.OccurredAtUtc)
            .Take(count)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToDto).ToList();
    }

    public async Task<IReadOnlyList<AuditRecordDto>> GetByTargetUserAsync(
        string targetUserId, int count, CancellationToken cancellationToken = default)
    {
        var entities = await dbContext.AuditRecords
            .AsNoTracking()
            .Where(e => e.TargetUserId == targetUserId)
            .OrderByDescending(static e => e.OccurredAtUtc)
            .Take(count)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToDto).ToList();
    }

    public async Task<IReadOnlyList<AuditRecordDto>> GetByCorrelationAsync(
        string correlationId, CancellationToken cancellationToken = default)
    {
        var entities = await dbContext.AuditRecords
            .AsNoTracking()
            .Where(e => e.CorrelationId == correlationId)
            .OrderByDescending(static e => e.OccurredAtUtc)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToDto).ToList();
    }

    public async Task<IReadOnlyList<AuditRecordDto>> GetByActionAsync(
        string action, int count, int offset, CancellationToken cancellationToken = default)
    {
        var entities = await dbContext.AuditRecords
            .AsNoTracking()
            .Where(e => e.Action == action)
            .OrderByDescending(static e => e.OccurredAtUtc)
            .Skip(offset)
            .Take(count)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToDto).ToList();
    }

    public async Task<long> CountByActionSinceAsync(
        string action, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        return await dbContext.AuditRecords
            .AsNoTracking()
            .Where(e => e.Action == action && e.OccurredAtUtc >= since)
            .LongCountAsync(cancellationToken);
    }

    private static AuditRecordDto MapToDto(AuditRecordEntity e) =>
        new(
            e.Id,
            e.Action,
            e.ActorId,
            e.TargetUserId,
            e.CorrelationId,
            e.IpAddress,
            e.UserAgent,
            e.Outcome,
            e.ReasonCode,
            e.MetadataJson,
            e.OccurredAtUtc);
}

internal sealed class ChannelAuditWriter(Channel<AuditRecord> channel) : IAuditChannelWriter
{
    public bool TryWrite(AuditRecord record) => channel.Writer.TryWrite(record);
}

internal sealed class AuditFlushBackgroundService(
    Channel<AuditRecord> channel,
    IServiceScopeFactory scopeFactory,
    ILogger<AuditFlushBackgroundService> logger) : BackgroundService
{
    private const int MaxBatchSize = 100;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<AuditRecord>(MaxBatchSize);
        var reader = channel.Reader;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var flushCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                flushCts.CancelAfter(FlushInterval);

                try
                {
                    while (batch.Count < MaxBatchSize)
                    {
                        var record = await reader.ReadAsync(flushCts.Token);
                        batch.Add(record);
                    }
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // Flush interval elapsed; flush whatever we have.
                }

                // Drain any remaining buffered items without waiting.
                while (batch.Count < MaxBatchSize && reader.TryRead(out var remaining))
                {
                    batch.Add(remaining);
                }

                if (batch.Count > 0)
                {
                    await FlushBatchAsync(batch, stoppingToken);
                    batch.Clear();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error flushing audit batch of {Count} records", batch.Count);
                batch.Clear();
            }
        }

        // Drain remaining items on shutdown.
        while (reader.TryRead(out var record))
        {
            batch.Add(record);
        }

        if (batch.Count > 0)
        {
            try
            {
                await FlushBatchAsync(batch, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error flushing final audit batch of {Count} records on shutdown", batch.Count);
            }
        }
    }

    private async Task FlushBatchAsync(List<AuditRecord> batch, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var entities = batch.Select(record => new AuditRecordEntity
        {
            Id = record.Id,
            Action = record.Action,
            ActorId = record.ActorId,
            TargetUserId = record.TargetUserId,
            CorrelationId = record.CorrelationId,
            IpAddress = record.IpAddress,
            UserAgent = record.UserAgent,
            Outcome = record.Outcome,
            ReasonCode = record.ReasonCode,
            MetadataJson = record.MetadataJson,
            OccurredAtUtc = record.OccurredAtUtc,
        });

        dbContext.AuditRecords.AddRange(entities);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogDebug("Flushed {Count} audit records", batch.Count);
    }
}

public static class AuditInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddAuditInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<AuditDbContext>((sp, options) =>
        {
            var postgresOptions = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;
            options.UseNpgsql(postgresOptions.ConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AuditDbContext.SchemaName));
        });

        var channel = Channel.CreateBounded<AuditRecord>(new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        services.AddSingleton(channel);
        services.AddScoped<IAuditWriter, EfAuditWriter>();
        services.AddScoped<IAuditReader, EfAuditReader>();
        services.AddSingleton<IAuditChannelWriter, ChannelAuditWriter>();
        services.AddHostedService<AuditFlushBackgroundService>();

        return services;
    }
}
