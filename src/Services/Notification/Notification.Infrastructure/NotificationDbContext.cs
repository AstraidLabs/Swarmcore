using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Notification.Domain;

namespace Notification.Infrastructure;

internal sealed class EmailOutboxEntity
{
    public Guid Id { get; set; }
    public string Recipient { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public string BodyText { get; set; } = string.Empty;
    public string? TemplateName { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ScheduledAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public int Status { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public string? CorrelationId { get; set; }
    public string? MetadataJson { get; set; }
}

internal sealed class EmailDeliveryAttemptEntity
{
    public Guid Id { get; set; }
    public Guid OutboxEntryId { get; set; }
    public DateTime AttemptedAtUtc { get; set; }
    public bool Succeeded { get; set; }
    public string? ErrorMessage { get; set; }
    public int? SmtpStatusCode { get; set; }
    public long DurationMs { get; set; }
}

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    internal DbSet<EmailOutboxEntity> EmailOutbox => Set<EmailOutboxEntity>();
    internal DbSet<EmailDeliveryAttemptEntity> EmailDeliveryAttempts => Set<EmailDeliveryAttemptEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("notification");

        modelBuilder.Entity<EmailOutboxEntity>(entity =>
        {
            entity.ToTable("email_outbox");
            entity.HasKey(static e => e.Id);
            entity.Property(static e => e.Id).HasColumnName("id");
            entity.Property(static e => e.Recipient).HasColumnName("recipient").HasMaxLength(512).IsRequired();
            entity.Property(static e => e.Subject).HasColumnName("subject").HasMaxLength(1024).IsRequired();
            entity.Property(static e => e.BodyHtml).HasColumnName("body_html").IsRequired();
            entity.Property(static e => e.BodyText).HasColumnName("body_text").IsRequired();
            entity.Property(static e => e.TemplateName).HasColumnName("template_name").HasMaxLength(256);
            entity.Property(static e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(static e => e.ScheduledAtUtc).HasColumnName("scheduled_at_utc");
            entity.Property(static e => e.ProcessedAtUtc).HasColumnName("processed_at_utc");
            entity.Property(static e => e.Status).HasColumnName("status");
            entity.Property(static e => e.RetryCount).HasColumnName("retry_count");
            entity.Property(static e => e.LastError).HasColumnName("last_error").HasMaxLength(4096);
            entity.Property(static e => e.CorrelationId).HasColumnName("correlation_id").HasMaxLength(256);
            entity.Property(static e => e.MetadataJson).HasColumnName("metadata_json");

            entity.HasIndex(static e => new { e.Status, e.ScheduledAtUtc })
                .HasDatabaseName("ix_email_outbox_status_scheduled_at");

            entity.HasIndex(static e => e.CorrelationId)
                .HasDatabaseName("ix_email_outbox_correlation_id");

            entity.HasIndex(static e => e.CreatedAtUtc)
                .HasDatabaseName("ix_email_outbox_created_at");
        });

        modelBuilder.Entity<EmailDeliveryAttemptEntity>(entity =>
        {
            entity.ToTable("email_delivery_attempts");
            entity.HasKey(static e => e.Id);
            entity.Property(static e => e.Id).HasColumnName("id");
            entity.Property(static e => e.OutboxEntryId).HasColumnName("outbox_entry_id");
            entity.Property(static e => e.AttemptedAtUtc).HasColumnName("attempted_at_utc");
            entity.Property(static e => e.Succeeded).HasColumnName("succeeded");
            entity.Property(static e => e.ErrorMessage).HasColumnName("error_message").HasMaxLength(4096);
            entity.Property(static e => e.SmtpStatusCode).HasColumnName("smtp_status_code");
            entity.Property(static e => e.DurationMs).HasColumnName("duration_ms");

            entity.HasIndex(static e => e.OutboxEntryId)
                .HasDatabaseName("ix_email_delivery_attempts_outbox_entry_id");
        });
    }
}

public sealed class NotificationDbContextFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public NotificationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<NotificationDbContext>();
        optionsBuilder.UseNpgsql(
            Environment.GetEnvironmentVariable("SWARMCORE_POSTGRES_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=swarmcore;Username=swarmcore;Password=swarmcore");
        return new NotificationDbContext(optionsBuilder.Options);
    }
}
