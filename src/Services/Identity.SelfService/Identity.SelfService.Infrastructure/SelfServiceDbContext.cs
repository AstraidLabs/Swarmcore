using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Identity.SelfService.Infrastructure;

public sealed class SelfServiceDbContext(DbContextOptions<SelfServiceDbContext> options) : DbContext(options)
{
    public const string SchemaName = "identity_selfservice";

    public DbSet<AdminAccountStateEntity> AdminAccountStates => Set<AdminAccountStateEntity>();
    public DbSet<VerificationTokenEntity> VerificationTokens => Set<VerificationTokenEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<AdminAccountStateEntity>(entity =>
        {
            entity.ToTable("admin_account_states");
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.UserId).HasColumnName("user_id").HasMaxLength(450);
            entity.Property(e => e.State).HasColumnName("state").HasMaxLength(50).IsRequired();
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.LastLoginAtUtc).HasColumnName("last_login_at_utc");
            entity.HasIndex(e => e.State);
        });

        modelBuilder.Entity<VerificationTokenEntity>(entity =>
        {
            entity.ToTable("verification_tokens");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(e => e.UserId).HasColumnName("user_id").HasMaxLength(450).IsRequired();
            entity.Property(e => e.Purpose).HasColumnName("purpose").HasMaxLength(50).IsRequired();
            entity.Property(e => e.TokenHash).HasColumnName("token_hash").HasMaxLength(256).IsRequired();
            entity.Property(e => e.ExpiresAtUtc).HasColumnName("expires_at_utc");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.ConsumedAtUtc).HasColumnName("consumed_at_utc");
            entity.Property(e => e.RevokedAtUtc).HasColumnName("revoked_at_utc");
            entity.HasIndex(e => new { e.TokenHash, e.Purpose }).HasDatabaseName("ix_verification_tokens_hash_purpose");
            entity.HasIndex(e => new { e.UserId, e.Purpose }).HasDatabaseName("ix_verification_tokens_user_purpose");
            entity.HasIndex(e => e.ExpiresAtUtc).HasDatabaseName("ix_verification_tokens_expires");
        });
    }
}

public sealed class AdminAccountStateEntity
{
    public string UserId { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? LastLoginAtUtc { get; set; }
}

public sealed class VerificationTokenEntity
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
}

public sealed class SelfServiceDbContextFactory : IDesignTimeDbContextFactory<SelfServiceDbContext>
{
    public SelfServiceDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=swarmcore;Username=swarmcore;Password=swarmcore";

        var optionsBuilder = new DbContextOptionsBuilder<SelfServiceDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", SelfServiceDbContext.SchemaName));

        return new SelfServiceDbContext(optionsBuilder.Options);
    }
}
