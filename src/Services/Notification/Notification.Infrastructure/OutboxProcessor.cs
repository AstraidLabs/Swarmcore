using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notification.Application;
using Notification.Domain;
using BeeTracker.BuildingBlocks.Abstractions.Options;

namespace Notification.Infrastructure;

internal sealed class EmailDispatchService(
    NotificationDbContext dbContext,
    IEmailTemplateRenderer templateRenderer) : IEmailDispatchService
{
    public async Task EnqueueAsync(EmailEnvelope envelope, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.Recipient);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.TemplateName);

        var rendered = await templateRenderer.RenderAsync(
            envelope.TemplateName,
            envelope.TemplateModel ?? new Dictionary<string, object>(),
            ct);

        var entry = EmailOutboxEntry.Create(
            recipient: envelope.Recipient,
            subject: rendered.Subject,
            bodyHtml: rendered.HtmlBody,
            bodyText: rendered.TextBody,
            templateName: envelope.TemplateName,
            correlationId: envelope.CorrelationId,
            metadata: null);

        var entity = new EmailOutboxEntity
        {
            Id = entry.Id,
            Recipient = entry.Recipient,
            Subject = entry.Subject,
            BodyHtml = entry.BodyHtml,
            BodyText = entry.BodyText,
            TemplateName = entry.TemplateName,
            CreatedAtUtc = entry.CreatedAtUtc,
            ScheduledAtUtc = entry.ScheduledAtUtc,
            ProcessedAtUtc = entry.ProcessedAtUtc,
            Status = (int)entry.Status,
            RetryCount = entry.RetryCount,
            LastError = entry.LastError,
            CorrelationId = entry.CorrelationId,
            MetadataJson = entry.MetadataJson,
        };

        dbContext.EmailOutbox.Add(entity);
        await dbContext.SaveChangesAsync(ct);
    }
}

internal sealed class OutboxProcessor(
    NotificationDbContext dbContext,
    IEmailSender emailSender,
    ILogger<OutboxProcessor> logger) : IOutboxProcessor
{
    private const int MaxRetryCount = 5;

    public async Task<int> ProcessPendingAsync(int batchSize, CancellationToken ct)
    {
        var pendingEntries = await dbContext.EmailOutbox
            .Where(e => (e.Status == (int)EmailOutboxStatus.Pending
                         || (e.Status == (int)EmailOutboxStatus.Failed && e.RetryCount < MaxRetryCount))
                        && (e.ScheduledAtUtc == null || e.ScheduledAtUtc <= DateTime.UtcNow))
            .OrderBy(e => e.CreatedAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);

        if (pendingEntries.Count == 0)
        {
            return 0;
        }

        var processedCount = 0;

        foreach (var entry in pendingEntries)
        {
            ct.ThrowIfCancellationRequested();

            entry.Status = (int)EmailOutboxStatus.Processing;
            await dbContext.SaveChangesAsync(ct);

            var stopwatch = Stopwatch.StartNew();

            var message = new EmailMessage(
                To: entry.Recipient,
                Subject: entry.Subject,
                HtmlBody: entry.BodyHtml,
                TextBody: entry.BodyText);

            EmailSendResult result;
            try
            {
                result = await emailSender.SendAsync(message, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result = new EmailSendResult(
                    Succeeded: false,
                    ErrorMessage: ex.Message,
                    SmtpStatusCode: null);
            }

            stopwatch.Stop();

            var attempt = new EmailDeliveryAttemptEntity
            {
                Id = Guid.NewGuid(),
                OutboxEntryId = entry.Id,
                AttemptedAtUtc = DateTime.UtcNow,
                Succeeded = result.Succeeded,
                ErrorMessage = result.ErrorMessage,
                SmtpStatusCode = result.SmtpStatusCode,
                DurationMs = stopwatch.ElapsedMilliseconds,
            };

            dbContext.EmailDeliveryAttempts.Add(attempt);

            if (result.Succeeded)
            {
                entry.Status = (int)EmailOutboxStatus.Sent;
                entry.ProcessedAtUtc = DateTime.UtcNow;
                entry.LastError = null;
                processedCount++;

                logger.LogInformation(
                    "Email {EntryId} sent to {Recipient} in {DurationMs}ms",
                    entry.Id, entry.Recipient, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                entry.RetryCount++;
                entry.LastError = result.ErrorMessage;
                entry.Status = entry.RetryCount >= MaxRetryCount
                    ? (int)EmailOutboxStatus.Failed
                    : (int)EmailOutboxStatus.Pending;

                logger.LogWarning(
                    "Email {EntryId} to {Recipient} failed (attempt {RetryCount}): {Error}",
                    entry.Id, entry.Recipient, entry.RetryCount, result.ErrorMessage);
            }

            await dbContext.SaveChangesAsync(ct);
        }

        return processedCount;
    }
}

internal sealed class OutboxProcessorBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxProcessorBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox processor background service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();

                var processed = await processor.ProcessPendingAsync(BatchSize, stoppingToken);

                if (processed > 0)
                {
                    logger.LogDebug("Outbox processor sent {Count} email(s)", processed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox processor encountered an error");
            }

            await Task.Delay(PollingInterval, stoppingToken);
        }

        logger.LogInformation("Outbox processor background service stopped");
    }
}

public static class NotificationInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddNotificationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<SmtpOptions>()
            .Bind(configuration.GetSection(SmtpOptions.SectionName))
            .ValidateOnStart();

        services.AddDbContext<NotificationDbContext>((sp, options) =>
        {
            var postgresOptions = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;
            options.UseNpgsql(postgresOptions.ConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "notification"));
        });

        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<IEmailTemplateRenderer, InMemoryEmailTemplateRenderer>();
        services.AddScoped<IEmailDispatchService, EmailDispatchService>();
        services.AddScoped<IOutboxProcessor, OutboxProcessor>();

        services.AddHostedService<OutboxProcessorBackgroundService>();

        return services;
    }
}
