using Audit.Domain;
using MediatR;

namespace Audit.Application;

public interface IAuditWriter
{
    Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default);
}

public interface IAuditChannelWriter
{
    bool TryWrite(AuditRecord record);
}

public interface IAuditReader
{
    Task<IReadOnlyList<AuditRecordDto>> GetRecentAsync(int count, int offset, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditRecordDto>> GetByActorAsync(string actorId, int count, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditRecordDto>> GetByTargetUserAsync(string targetUserId, int count, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditRecordDto>> GetByCorrelationAsync(string correlationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditRecordDto>> GetByActionAsync(string action, int count, int offset, CancellationToken cancellationToken = default);
    Task<long> CountByActionSinceAsync(string action, DateTimeOffset since, CancellationToken cancellationToken = default);
}

public sealed record AuditRecordDto(
    Guid Id,
    string Action,
    string? ActorId,
    string? TargetUserId,
    string? CorrelationId,
    string? IpAddress,
    string? UserAgent,
    AuditOutcome Outcome,
    string? ReasonCode,
    string? MetadataJson,
    DateTimeOffset OccurredAtUtc);

public sealed record AuditEventNotification(AuditRecord Record) : INotification;

internal sealed class AuditEventNotificationHandler(IAuditChannelWriter channelWriter)
    : INotificationHandler<AuditEventNotification>
{
    public Task Handle(AuditEventNotification notification, CancellationToken cancellationToken)
    {
        channelWriter.TryWrite(notification.Record);
        return Task.CompletedTask;
    }
}
