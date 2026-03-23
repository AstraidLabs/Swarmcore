using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Swarmcore.Caching.Redis;
using Swarmcore.Contracts.Configuration;
using Tracker.ConfigurationService.Application;

namespace Tracker.ConfigurationService.Infrastructure;

internal sealed record AuditWriteRequest(
    DateTimeOffset OccurredAtUtc,
    string ActorId,
    string ActorRole,
    string Action,
    string Severity,
    string EntityType,
    string EntityId,
    string CorrelationId,
    string? RequestId,
    string Result,
    string? IpAddress,
    string? UserAgent,
    string? BeforeJson,
    string? AfterJson);

internal static class AuditSeverityResolver
{
    public static string Resolve(string action) => action switch
    {
        "torrent_policy.upsert" => "high",
        "torrent.activate" => "high",
        "torrent.deactivate" => "high",
        "passkey.upsert" => "high",
        "passkey.revoke" => "critical",
        "passkey.rotate" => "critical",
        "passkey.bulk_revoke" => "critical",
        "passkey.bulk_rotate" => "critical",
        "permissions.upsert" => "high",
        "permissions.bulk_upsert" => "high",
        "ban.upsert" => "high",
        "ban.expire" => "high",
        "ban.delete" => "high",
        "ban.bulk_upsert" => "high",
        "ban.bulk_expire" => "high",
        "ban.bulk_delete" => "high",
        "maintenance.trigger" => "medium",
        "node.state_transition" => "high",
        "config.export" => "medium",
        "config.restore" => "critical",
        "governance.policy_change" => "critical",
        _ => "medium"
    };
}

internal interface IAuditBuffer
{
    ValueTask EnqueueAsync(AuditWriteRequest auditWriteRequest, CancellationToken cancellationToken);
    IAsyncEnumerable<IReadOnlyCollection<AuditWriteRequest>> ReadBatchesAsync(CancellationToken cancellationToken);
}

internal sealed class ChannelAuditBuffer : IAuditBuffer
{
    private readonly System.Threading.Channels.Channel<AuditWriteRequest> _channel =
        System.Threading.Channels.Channel.CreateBounded<AuditWriteRequest>(new System.Threading.Channels.BoundedChannelOptions(4096)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest
        });

    public ValueTask EnqueueAsync(AuditWriteRequest auditWriteRequest, CancellationToken cancellationToken)
        => _channel.Writer.WriteAsync(auditWriteRequest, cancellationToken);

    public async IAsyncEnumerable<IReadOnlyCollection<AuditWriteRequest>> ReadBatchesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = new List<AuditWriteRequest>(128);

        while (!cancellationToken.IsCancellationRequested)
        {
            while (buffer.Count < 128 && await _channel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (buffer.Count < 128 && _channel.Reader.TryRead(out var item))
                {
                    buffer.Add(item);
                }

                if (buffer.Count > 0)
                {
                    break;
                }
            }

            if (buffer.Count == 0)
            {
                continue;
            }

            yield return buffer.ToArray();
            buffer.Clear();
        }
    }
}

public sealed class TrackerConfigurationDbContext(DbContextOptions<TrackerConfigurationDbContext> options) : DbContext(options)
{
    internal DbSet<TorrentConfigurationEntity> Torrents => Set<TorrentConfigurationEntity>();
    internal DbSet<TorrentPolicyEntity> TorrentPolicies => Set<TorrentPolicyEntity>();
    internal DbSet<PasskeyCredentialEntity> Passkeys => Set<PasskeyCredentialEntity>();
    internal DbSet<UserPermissionEntity> Permissions => Set<UserPermissionEntity>();
    internal DbSet<BanRuleEntity> Bans => Set<BanRuleEntity>();
    internal DbSet<AuditRecordEntity> AuditRecords => Set<AuditRecordEntity>();
    internal DbSet<MaintenanceRunEntity> MaintenanceRuns => Set<MaintenanceRunEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TorrentConfigurationEntity>(entity =>
        {
            entity.ToTable("torrents");
            entity.HasKey(static torrent => torrent.Id);
            entity.Property(static torrent => torrent.Id).HasColumnName("id");
            entity.Property(static torrent => torrent.InfoHash).HasColumnName("info_hash").HasMaxLength(40);
            entity.Property(static torrent => torrent.IsPrivate).HasColumnName("is_private");
            entity.Property(static torrent => torrent.IsEnabled).HasColumnName("is_enabled");
            entity.HasIndex(static torrent => torrent.InfoHash).IsUnique();
        });

        modelBuilder.Entity<TorrentPolicyEntity>(entity =>
        {
            entity.ToTable("torrent_policies");
            entity.HasKey(static policy => policy.TorrentId);
            entity.Property(static policy => policy.TorrentId).HasColumnName("torrent_id");
            entity.Property(static policy => policy.AnnounceIntervalSeconds).HasColumnName("announce_interval_seconds");
            entity.Property(static policy => policy.MinAnnounceIntervalSeconds).HasColumnName("min_announce_interval_seconds");
            entity.Property(static policy => policy.DefaultNumWant).HasColumnName("default_numwant");
            entity.Property(static policy => policy.MaxNumWant).HasColumnName("max_numwant");
            entity.Property(static policy => policy.AllowScrape).HasColumnName("allow_scrape");
            entity.Property(static policy => policy.WarningMessage).HasColumnName("warning_message").HasMaxLength(512);
            entity.Property(static policy => policy.RowVersion).HasColumnName("row_version");
            entity.HasOne(static policy => policy.Torrent)
                .WithOne(static torrent => torrent.Policy)
                .HasForeignKey<TorrentPolicyEntity>(static policy => policy.TorrentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PasskeyCredentialEntity>(entity =>
        {
            entity.ToTable("passkeys");
            entity.HasKey(static passkey => passkey.Passkey);
            entity.Property(static passkey => passkey.Passkey).HasColumnName("passkey").HasMaxLength(64);
            entity.Property(static passkey => passkey.UserId).HasColumnName("user_id");
            entity.Property(static passkey => passkey.IsRevoked).HasColumnName("is_revoked");
            entity.Property(static passkey => passkey.ExpiresAtUtc).HasColumnName("expires_at_utc");
            entity.Property(static passkey => passkey.RowVersion).HasColumnName("row_version");
            entity.HasIndex(static passkey => passkey.UserId);
        });

        modelBuilder.Entity<UserPermissionEntity>(entity =>
        {
            entity.ToTable("permissions");
            entity.HasKey(static permission => permission.UserId);
            entity.Property(static permission => permission.UserId).HasColumnName("user_id");
            entity.Property(static permission => permission.CanLeech).HasColumnName("can_leech");
            entity.Property(static permission => permission.CanSeed).HasColumnName("can_seed");
            entity.Property(static permission => permission.CanScrape).HasColumnName("can_scrape");
            entity.Property(static permission => permission.CanUsePrivateTracker).HasColumnName("can_use_private_tracker");
            entity.Property(static permission => permission.RowVersion).HasColumnName("row_version");
        });

        modelBuilder.Entity<BanRuleEntity>(entity =>
        {
            entity.ToTable("bans");
            entity.HasKey(static ban => new { ban.Scope, ban.Subject });
            entity.Property(static ban => ban.Scope).HasColumnName("scope").HasMaxLength(64);
            entity.Property(static ban => ban.Subject).HasColumnName("subject").HasMaxLength(256);
            entity.Property(static ban => ban.Reason).HasColumnName("reason").HasMaxLength(512);
            entity.Property(static ban => ban.ExpiresAtUtc).HasColumnName("expires_at_utc");
            entity.Property(static ban => ban.RowVersion).HasColumnName("row_version");
            entity.HasIndex(static ban => ban.ExpiresAtUtc);
        });

        modelBuilder.Entity<AuditRecordEntity>(entity =>
        {
            entity.ToTable("audit_records");
            entity.HasKey(static audit => audit.Id);
            entity.Property(static audit => audit.Id).HasColumnName("id");
            entity.Property(static audit => audit.OccurredAtUtc).HasColumnName("occurred_at_utc");
            entity.Property(static audit => audit.ActorId).HasColumnName("actor_id").HasMaxLength(256);
            entity.Property(static audit => audit.ActorRole).HasColumnName("actor_role").HasMaxLength(64);
            entity.Property(static audit => audit.Action).HasColumnName("action").HasMaxLength(128);
            entity.Property(static audit => audit.Severity).HasColumnName("severity").HasMaxLength(32);
            entity.Property(static audit => audit.EntityType).HasColumnName("entity_type").HasMaxLength(64);
            entity.Property(static audit => audit.EntityId).HasColumnName("entity_id").HasMaxLength(256);
            entity.Property(static audit => audit.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
            entity.Property(static audit => audit.RequestId).HasColumnName("request_id").HasMaxLength(128);
            entity.Property(static audit => audit.Result).HasColumnName("result").HasMaxLength(32);
            entity.Property(static audit => audit.IpAddress).HasColumnName("ip_address").HasMaxLength(128);
            entity.Property(static audit => audit.UserAgent).HasColumnName("user_agent").HasMaxLength(512);
            entity.Property(static audit => audit.BeforeJson).HasColumnName("before_json").HasColumnType("jsonb");
            entity.Property(static audit => audit.AfterJson).HasColumnName("after_json").HasColumnType("jsonb");
            entity.HasIndex(static audit => audit.OccurredAtUtc);
        });

        modelBuilder.Entity<MaintenanceRunEntity>(entity =>
        {
            entity.ToTable("maintenance_runs");
            entity.HasKey(static maintenance => maintenance.Id);
            entity.Property(static maintenance => maintenance.Id).HasColumnName("id");
            entity.Property(static maintenance => maintenance.Operation).HasColumnName("operation").HasMaxLength(128);
            entity.Property(static maintenance => maintenance.RequestedBy).HasColumnName("requested_by").HasMaxLength(256);
            entity.Property(static maintenance => maintenance.RequestedAtUtc).HasColumnName("requested_at_utc");
            entity.Property(static maintenance => maintenance.Status).HasColumnName("status").HasMaxLength(32);
            entity.Property(static maintenance => maintenance.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
            entity.HasIndex(static maintenance => maintenance.RequestedAtUtc);
        });
    }
}

public sealed class TorrentConfigurationReader(TrackerConfigurationDbContext dbContext) : ITorrentConfigurationReader
{
    public async Task<IReadOnlyCollection<TorrentPolicyDto>> GetTorrentPoliciesAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Torrents
            .AsNoTracking()
            .Select(static torrent => new TorrentPolicyDto(
                torrent.InfoHash,
                torrent.IsPrivate,
                torrent.IsEnabled,
                1800,
                900,
                50,
                100,
                true,
                1))
            .ToListAsync(cancellationToken);
    }
}

public sealed class RedisConfigurationCacheInvalidationPublisher(IRedisCacheClient redisCacheClient) : IConfigurationCacheInvalidationPublisher
{
    private const string ChannelName = "tracker:cache-invalidation";

    public Task PublishTorrentPolicyInvalidationAsync(string infoHashHex, CancellationToken cancellationToken)
        => PublishAsync(new ConfigurationCacheInvalidationMessage(ConfigurationCacheInvalidationKind.TorrentPolicy, infoHashHex));

    public Task PublishPasskeyInvalidationAsync(string passkey, CancellationToken cancellationToken)
        => PublishAsync(new ConfigurationCacheInvalidationMessage(ConfigurationCacheInvalidationKind.Passkey, passkey));

    public Task PublishUserPermissionInvalidationAsync(Guid userId, CancellationToken cancellationToken)
        => PublishAsync(new ConfigurationCacheInvalidationMessage(ConfigurationCacheInvalidationKind.UserPermission, userId.ToString("D")));

    public Task PublishBanRuleInvalidationAsync(string scope, string subject, CancellationToken cancellationToken)
        => PublishAsync(new ConfigurationCacheInvalidationMessage(ConfigurationCacheInvalidationKind.BanRule, $"{scope}|{subject}"));

    private Task PublishAsync(ConfigurationCacheInvalidationMessage message)
        => redisCacheClient.Subscriber.PublishAsync(RedisChannel.Literal(ChannelName), System.Text.Json.JsonSerializer.Serialize(message));
}

public static class ConfigurationInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddConfigurationInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<TrackerConfigurationDbContext>((serviceProvider, options) =>
        {
            var postgresOptions = serviceProvider.GetRequiredService<IOptions<Swarmcore.BuildingBlocks.Abstractions.Options.PostgresOptions>>().Value;
            options.UseNpgsql(postgresOptions.ConnectionString);
        });

        services.AddSingleton<IAuditBuffer, ChannelAuditBuffer>();
        services.AddScoped<ITorrentConfigurationReader, TorrentConfigurationReader>();
        services.AddScoped<EfConfigurationMutationService>();
        services.AddScoped<IConfigurationMutationService>(static serviceProvider => serviceProvider.GetRequiredService<EfConfigurationMutationService>());
        services.AddScoped<IConfigurationMutationPreviewService>(static serviceProvider => serviceProvider.GetRequiredService<EfConfigurationMutationService>());
        services.AddScoped<IConfigurationMaintenanceService, EfConfigurationMaintenanceService>();
        services.AddSingleton<IConfigurationCacheInvalidationPublisher, RedisConfigurationCacheInvalidationPublisher>();
        services.AddHostedService<EfAuditWriterBackgroundService>();
        return services;
    }
}
