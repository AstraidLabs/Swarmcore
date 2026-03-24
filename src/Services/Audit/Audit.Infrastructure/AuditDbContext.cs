using Audit.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Audit.Infrastructure;

internal sealed class AuditRecordEntity
{
    public Guid Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? ActorId { get; set; }
    public string? TargetUserId { get; set; }
    public string? CorrelationId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public AuditOutcome Outcome { get; set; }
    public string? ReasonCode { get; set; }
    public string? MetadataJson { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}

public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    internal const string SchemaName = "audit";

    internal DbSet<AuditRecordEntity> AuditRecords => Set<AuditRecordEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<AuditRecordEntity>(entity =>
        {
            entity.ToTable("audit_records");

            entity.HasKey(static e => e.Id);
            entity.Property(static e => e.Id).HasColumnName("id");

            entity.Property(static e => e.Action)
                .HasColumnName("action")
                .HasMaxLength(256)
                .IsRequired();

            entity.Property(static e => e.ActorId)
                .HasColumnName("actor_id")
                .HasMaxLength(256);

            entity.Property(static e => e.TargetUserId)
                .HasColumnName("target_user_id")
                .HasMaxLength(256);

            entity.Property(static e => e.CorrelationId)
                .HasColumnName("correlation_id")
                .HasMaxLength(256);

            entity.Property(static e => e.IpAddress)
                .HasColumnName("ip_address")
                .HasMaxLength(45);

            entity.Property(static e => e.UserAgent)
                .HasColumnName("user_agent")
                .HasMaxLength(512);

            entity.Property(static e => e.Outcome)
                .HasColumnName("outcome")
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.Property(static e => e.ReasonCode)
                .HasColumnName("reason_code")
                .HasMaxLength(256);

            entity.Property(static e => e.MetadataJson)
                .HasColumnName("metadata_json")
                .HasColumnType("jsonb");

            entity.Property(static e => e.OccurredAtUtc)
                .HasColumnName("occurred_at_utc");

            entity.HasIndex(static e => e.OccurredAtUtc);
            entity.HasIndex(static e => e.ActorId);
            entity.HasIndex(static e => e.TargetUserId);
            entity.HasIndex(static e => e.CorrelationId);
            entity.HasIndex(static e => e.Action);
        });
    }
}

public sealed class AuditDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AuditDbContext>();
        optionsBuilder.UseNpgsql(
            Environment.GetEnvironmentVariable("BEETRACKER_POSTGRES_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=beetracker;Username=beetracker;Password=beetracker",
            npgsqlOptions => npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", AuditDbContext.SchemaName));
        return new AuditDbContext(optionsBuilder.Options);
    }
}
