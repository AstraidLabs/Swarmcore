using Swarmcore.BuildingBlocks.Domain.Primitives;

namespace Notification.Domain;

public enum EmailOutboxStatus
{
    Pending = 0,
    Processing = 1,
    Sent = 2,
    Failed = 3,
    Cancelled = 4,
}

public sealed class EmailOutboxEntry : Entity<Guid>
{
    private EmailOutboxEntry(Guid id) : base(id) { }

    public string Recipient { get; private set; } = string.Empty;
    public string Subject { get; private set; } = string.Empty;
    public string BodyHtml { get; private set; } = string.Empty;
    public string BodyText { get; private set; } = string.Empty;
    public string? TemplateName { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ScheduledAtUtc { get; private set; }
    public DateTime? ProcessedAtUtc { get; private set; }
    public EmailOutboxStatus Status { get; private set; }
    public int RetryCount { get; private set; }
    public string? LastError { get; private set; }
    public string? CorrelationId { get; private set; }
    public string? MetadataJson { get; private set; }

    public static EmailOutboxEntry Create(
        string recipient,
        string subject,
        string bodyHtml,
        string bodyText,
        string? templateName,
        string? correlationId,
        string? metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        return new EmailOutboxEntry(Guid.NewGuid())
        {
            Recipient = recipient,
            Subject = subject,
            BodyHtml = bodyHtml,
            BodyText = bodyText,
            TemplateName = templateName,
            CreatedAtUtc = DateTime.UtcNow,
            ScheduledAtUtc = null,
            ProcessedAtUtc = null,
            Status = EmailOutboxStatus.Pending,
            RetryCount = 0,
            LastError = null,
            CorrelationId = correlationId,
            MetadataJson = metadata,
        };
    }

    public void MarkProcessing()
    {
        Status = EmailOutboxStatus.Processing;
    }

    public void MarkSent()
    {
        Status = EmailOutboxStatus.Sent;
        ProcessedAtUtc = DateTime.UtcNow;
    }

    public void MarkFailed(string error)
    {
        Status = EmailOutboxStatus.Failed;
        LastError = error;
        RetryCount++;
    }

    public void Cancel()
    {
        Status = EmailOutboxStatus.Cancelled;
    }
}

public sealed class EmailDeliveryAttempt : Entity<Guid>
{
    private EmailDeliveryAttempt(Guid id) : base(id) { }

    public Guid OutboxEntryId { get; private set; }
    public DateTime AttemptedAtUtc { get; private set; }
    public bool Succeeded { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int? SmtpStatusCode { get; private set; }
    public long DurationMs { get; private set; }

    public static EmailDeliveryAttempt Record(
        Guid outboxEntryId,
        bool succeeded,
        string? errorMessage,
        int? smtpStatusCode,
        long durationMs)
    {
        return new EmailDeliveryAttempt(Guid.NewGuid())
        {
            OutboxEntryId = outboxEntryId,
            AttemptedAtUtc = DateTime.UtcNow,
            Succeeded = succeeded,
            ErrorMessage = errorMessage,
            SmtpStatusCode = smtpStatusCode,
            DurationMs = durationMs,
        };
    }
}
