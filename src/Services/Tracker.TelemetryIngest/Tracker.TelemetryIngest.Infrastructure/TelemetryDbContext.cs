using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Tracker.TelemetryIngest.Infrastructure;

internal sealed class AnnounceTelemetryEntity
{
    public long Id { get; set; }
    public string NodeId { get; set; } = string.Empty;
    public string InfoHash { get; set; } = string.Empty;
    public string Passkey { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public int RequestedPeers { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}

public sealed class TelemetryDbContext(DbContextOptions<TelemetryDbContext> options) : DbContext(options)
{
    internal DbSet<AnnounceTelemetryEntity> AnnounceTelemetry => Set<AnnounceTelemetryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AnnounceTelemetryEntity>(entity =>
        {
            entity.ToTable("announce_telemetry");
            entity.HasKey(static telemetry => telemetry.Id);
            entity.Property(static telemetry => telemetry.Id).ValueGeneratedOnAdd();
            entity.Property(static telemetry => telemetry.NodeId).HasMaxLength(128);
            entity.Property(static telemetry => telemetry.InfoHash).HasMaxLength(40);
            entity.Property(static telemetry => telemetry.Passkey).HasMaxLength(128);
            entity.Property(static telemetry => telemetry.EventName).HasMaxLength(32);
            entity.HasIndex(static telemetry => telemetry.OccurredAtUtc);
            entity.HasIndex(static telemetry => telemetry.InfoHash);
        });
    }
}

public sealed class TelemetryDbContextFactory : IDesignTimeDbContextFactory<TelemetryDbContext>
{
    public TelemetryDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TelemetryDbContext>();
        optionsBuilder.UseNpgsql(
            Environment.GetEnvironmentVariable("BEETRACKER_POSTGRES_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=beetracker;Username=beetracker;Password=beetracker");
        return new TelemetryDbContext(optionsBuilder.Options);
    }
}
