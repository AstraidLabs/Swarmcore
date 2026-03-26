using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Identity.SelfService.Infrastructure;

public sealed class SelfServiceDbContext(DbContextOptions<SelfServiceDbContext> options) : DbContext(options)
{
    public const string SchemaName = "identity_selfservice";

    public DbSet<AdminAccountStateEntity> AdminAccountStates => Set<AdminAccountStateEntity>();
    public DbSet<VerificationTokenEntity> VerificationTokens => Set<VerificationTokenEntity>();
    public DbSet<AdminUserProfileEntity> AdminUserProfiles => Set<AdminUserProfileEntity>();
    public DbSet<PermissionDefinitionEntity> PermissionDefinitions => Set<PermissionDefinitionEntity>();
    public DbSet<PermissionGroupEntity> PermissionGroups => Set<PermissionGroupEntity>();
    public DbSet<PermissionGroupItemEntity> PermissionGroupItems => Set<PermissionGroupItemEntity>();
    public DbSet<RolePermissionGroupEntity> RolePermissionGroups => Set<RolePermissionGroupEntity>();
    public DbSet<RolePermissionEntity> RolePermissions => Set<RolePermissionEntity>();
    public DbSet<RoleMetadataEntity> RoleMetadata => Set<RoleMetadataEntity>();
    public DbSet<RbacStateEntity> RbacStates => Set<RbacStateEntity>();

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

        modelBuilder.Entity<AdminUserProfileEntity>(entity =>
        {
            entity.ToTable("admin_user_profiles");
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.UserId).HasColumnName("user_id").HasMaxLength(450);
            entity.Property(e => e.DisplayName).HasColumnName("display_name").HasMaxLength(256).IsRequired();
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.Property(e => e.TimeZone).HasColumnName("time_zone").HasMaxLength(100).IsRequired();
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
        });

        modelBuilder.Entity<PermissionDefinitionEntity>(entity =>
        {
            entity.ToTable("permission_definitions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(e => e.Key).HasColumnName("key").HasMaxLength(128).IsRequired();
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(256).IsRequired();
            entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(1024);
            entity.Property(e => e.Category).HasColumnName("category").HasMaxLength(128).IsRequired();
            entity.Property(e => e.IsSystemPermission).HasColumnName("is_system_permission");
            entity.HasIndex(e => e.Key).IsUnique().HasDatabaseName("ix_permission_definitions_key");
        });

        modelBuilder.Entity<PermissionGroupEntity>(entity =>
        {
            entity.ToTable("permission_groups");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(256).IsRequired();
            entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(1024);
            entity.Property(e => e.IsSystemGroup).HasColumnName("is_system_group");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.HasIndex(e => e.Name).IsUnique().HasDatabaseName("ix_permission_groups_name");
        });

        modelBuilder.Entity<PermissionGroupItemEntity>(entity =>
        {
            entity.ToTable("permission_group_items");
            entity.HasKey(e => new { e.PermissionGroupId, e.PermissionDefinitionId });
            entity.Property(e => e.PermissionGroupId).HasColumnName("permission_group_id");
            entity.Property(e => e.PermissionDefinitionId).HasColumnName("permission_definition_id");
        });

        modelBuilder.Entity<RolePermissionGroupEntity>(entity =>
        {
            entity.ToTable("role_permission_groups");
            entity.HasKey(e => new { e.RoleId, e.PermissionGroupId });
            entity.Property(e => e.RoleId).HasColumnName("role_id").HasMaxLength(450);
            entity.Property(e => e.PermissionGroupId).HasColumnName("permission_group_id");
        });

        modelBuilder.Entity<RolePermissionEntity>(entity =>
        {
            entity.ToTable("role_permissions");
            entity.HasKey(e => new { e.RoleId, e.PermissionDefinitionId });
            entity.Property(e => e.RoleId).HasColumnName("role_id").HasMaxLength(450);
            entity.Property(e => e.PermissionDefinitionId).HasColumnName("permission_definition_id");
        });

        modelBuilder.Entity<RoleMetadataEntity>(entity =>
        {
            entity.ToTable("role_metadata");
            entity.HasKey(e => e.RoleId);
            entity.Property(e => e.RoleId).HasColumnName("role_id").HasMaxLength(450);
            entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(1024);
            entity.Property(e => e.IsSystemRole).HasColumnName("is_system_role");
            entity.Property(e => e.Priority).HasColumnName("priority");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
        });

        modelBuilder.Entity<RbacStateEntity>(entity =>
        {
            entity.ToTable("rbac_state");
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).HasColumnName("key").HasMaxLength(128);
            entity.Property(e => e.Version).HasColumnName("version");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
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

// ─── RBAC Entities ──────────────────────────────────────────────────────────

public sealed class AdminUserProfileEntity
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string TimeZone { get; set; } = "UTC";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class PermissionDefinitionEntity
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsSystemPermission { get; set; }
}

public sealed class PermissionGroupEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsSystemGroup { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class PermissionGroupItemEntity
{
    public Guid PermissionGroupId { get; set; }
    public Guid PermissionDefinitionId { get; set; }
}

public sealed class RolePermissionGroupEntity
{
    public string RoleId { get; set; } = string.Empty;
    public Guid PermissionGroupId { get; set; }
}

public sealed class RolePermissionEntity
{
    public string RoleId { get; set; } = string.Empty;
    public Guid PermissionDefinitionId { get; set; }
}

public sealed class RoleMetadataEntity
{
    public string RoleId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsSystemRole { get; set; }
    public int Priority { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class RbacStateEntity
{
    public string Key { get; set; } = string.Empty;
    public long Version { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class SelfServiceDbContextFactory : IDesignTimeDbContextFactory<SelfServiceDbContext>
{
    public SelfServiceDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=beetracker;Username=beetracker;Password=beetracker";

        var optionsBuilder = new DbContextOptionsBuilder<SelfServiceDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", SelfServiceDbContext.SchemaName));

        return new SelfServiceDbContext(optionsBuilder.Options);
    }
}
