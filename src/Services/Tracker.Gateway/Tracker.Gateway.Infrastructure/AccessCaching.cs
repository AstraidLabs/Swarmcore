using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using StackExchange.Redis;
using BeeTracker.BuildingBlocks.Abstractions.Options;
using BeeTracker.BuildingBlocks.Observability.Diagnostics;
using BeeTracker.Caching.Redis;
using BeeTracker.Contracts.Configuration;
using BeeTracker.Persistence.Postgres;
using Tracker.Gateway.Application.Announce;

namespace Tracker.Gateway.Infrastructure;

internal enum AccessRefreshKind : byte
{
    TorrentPolicy = 1,
    Passkey = 2,
    UserPermission = 3,
    BanRule = 4
}

internal readonly record struct AccessRefreshRequest(AccessRefreshKind Kind, string Key);

internal readonly record struct AccessCacheEnvelope<T>(bool Found, T? Value);

public sealed class AccessRefreshQueue
{
    private readonly Channel<AccessRefreshRequest> _channel = Channel.CreateBounded<AccessRefreshRequest>(new BoundedChannelOptions(16_384)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropOldest
    });

    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.Ordinal);

    internal ChannelReader<AccessRefreshRequest> Reader => _channel.Reader;

    public void EnqueueTorrentPolicy(string infoHashHex)
    {
        Enqueue(new AccessRefreshRequest(AccessRefreshKind.TorrentPolicy, infoHashHex));
    }

    public void EnqueuePasskey(string passkey)
    {
        Enqueue(new AccessRefreshRequest(AccessRefreshKind.Passkey, passkey));
    }

    public void EnqueueUserPermission(Guid userId)
    {
        Enqueue(new AccessRefreshRequest(AccessRefreshKind.UserPermission, userId.ToString("D")));
    }

    public void EnqueueBanRule(string scope, string subject)
    {
        Enqueue(new AccessRefreshRequest(AccessRefreshKind.BanRule, ComposeBanRuleKey(scope, subject)));
    }

    internal void Complete(AccessRefreshRequest request)
    {
        _pending.TryRemove($"{(byte)request.Kind}:{request.Key}", out _);
    }

    private void Enqueue(AccessRefreshRequest request)
    {
        var dedupeKey = $"{(byte)request.Kind}:{request.Key}";
        if (!_pending.TryAdd(dedupeKey, 0))
        {
            return;
        }

        if (!_channel.Writer.TryWrite(request))
        {
            _pending.TryRemove(dedupeKey, out _);
        }
    }

    internal static string ComposeBanRuleKey(string scope, string subject) => $"{scope}|{subject}";

    internal static bool TryParseBanRuleKey(string key, out string scope, out string subject)
    {
        var separator = key.IndexOf('|');
        if (separator <= 0 || separator >= key.Length - 1)
        {
            scope = string.Empty;
            subject = string.Empty;
            return false;
        }

        scope = key[..separator];
        subject = key[(separator + 1)..];
        return true;
    }
}

internal interface IAccessSnapshotStore
{
    ValueTask<AccessCacheEnvelope<TorrentPolicyDto>> GetTorrentPolicyAsync(string infoHashHex, CancellationToken cancellationToken);
    ValueTask<AccessCacheEnvelope<PasskeyAccessDto>> GetPasskeyAsync(string passkey, CancellationToken cancellationToken);
    ValueTask<AccessCacheEnvelope<TrackerAccessRightsDto>> GetTrackerAccessRightsAsync(Guid userId, CancellationToken cancellationToken);
    ValueTask<AccessCacheEnvelope<BanRuleDto>> GetBanRuleAsync(string scope, string subject, CancellationToken cancellationToken);
}

internal sealed class RedisPostgresAccessSnapshotStore(
    IRedisCacheClient redisCacheClient,
    IPostgresConnectionFactory postgresConnectionFactory,
    IOptions<PolicyCacheOptions> cacheOptions,
    ILogger<RedisPostgresAccessSnapshotStore> logger) : IAccessSnapshotStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly KeyValuePair<string, object?> L2Tag = new("layer", "L2");

    public async ValueTask<AccessCacheEnvelope<TorrentPolicyDto>> GetTorrentPolicyAsync(string infoHashHex, CancellationToken cancellationToken)
    {
        var redisKey = $"tracker:policy:{infoHashHex}";
        try
        {
            var cached = await redisCacheClient.Database.StringGetAsync(redisKey);
            if (cached.HasValue)
            {
                TrackerDiagnostics.CacheHit.Add(1, L2Tag);
                var cachedPolicy = JsonSerializer.Deserialize<TorrentPolicyDto>((string)cached!, SerializerOptions);
                return new AccessCacheEnvelope<TorrentPolicyDto>(cachedPolicy is not null, cachedPolicy);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis L2 read failed for torrent policy {InfoHash}.", infoHashHex);
        }

        await using var connection = await postgresConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                t.info_hash,
                t.is_private,
                t.is_enabled,
                coalesce(tp.announce_interval_seconds, 1800),
                coalesce(tp.min_announce_interval_seconds, 900),
                coalesce(tp.default_numwant, 50),
                coalesce(tp.max_numwant, 80),
                coalesce(tp.allow_scrape, true),
                coalesce(tp.row_version, 1),
                tp.warning_message
            from torrents t
            left join torrent_policies tp on tp.torrent_id = t.id
            where t.info_hash = $1
            limit 1
            """;
        command.Parameters.Add(new NpgsqlParameter { Value = infoHashHex });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new AccessCacheEnvelope<TorrentPolicyDto>(false, null);
        }

        var loadedPolicy = new TorrentPolicyDto(
            reader.GetString(0),
            reader.GetBoolean(1),
            reader.GetBoolean(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetBoolean(7),
            reader.GetInt64(8),
            reader.IsDBNull(9) ? null : reader.GetString(9));

        try
        {
            await redisCacheClient.Database.StringSetAsync(
                redisKey,
                JsonSerializer.Serialize(loadedPolicy, SerializerOptions),
                TimeSpan.FromSeconds(cacheOptions.Value.L2Seconds));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis L2 write failed for torrent policy {InfoHash}.", infoHashHex);
        }

        return new AccessCacheEnvelope<TorrentPolicyDto>(true, loadedPolicy);
    }

    public async ValueTask<AccessCacheEnvelope<PasskeyAccessDto>> GetPasskeyAsync(string passkey, CancellationToken cancellationToken)
    {
        var redisKey = $"tracker:passkey:{passkey}";
        try
        {
            var cached = await redisCacheClient.Database.StringGetAsync(redisKey);
            if (cached.HasValue)
            {
                TrackerDiagnostics.CacheHit.Add(1, L2Tag);
                var cachedAccess = JsonSerializer.Deserialize<PasskeyAccessDto>((string)cached!, SerializerOptions);
                return new AccessCacheEnvelope<PasskeyAccessDto>(cachedAccess is not null, cachedAccess);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis L2 read failed for passkey.");
        }

        await using var connection = await postgresConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                p.id,
                p.passkey,
                p.user_id,
                p.is_revoked,
                p.expires_at_utc,
                p.row_version
            from passkeys p
            where p.passkey = $1
            limit 1
            """;
        command.Parameters.Add(new NpgsqlParameter { Value = passkey });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new AccessCacheEnvelope<PasskeyAccessDto>(false, null);
        }

        var loadedAccess = new PasskeyAccessDto(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetGuid(2),
            reader.GetBoolean(3),
            reader.IsDBNull(4) ? null : new DateTimeOffset(reader.GetFieldValue<DateTime>(4), TimeSpan.Zero),
            reader.GetInt64(5));

        try
        {
            await redisCacheClient.Database.StringSetAsync(
                redisKey,
                JsonSerializer.Serialize(loadedAccess, SerializerOptions),
                TimeSpan.FromSeconds(cacheOptions.Value.L2Seconds));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis L2 write failed for passkey.");
        }

        return new AccessCacheEnvelope<PasskeyAccessDto>(true, loadedAccess);
    }

    public async ValueTask<AccessCacheEnvelope<TrackerAccessRightsDto>> GetTrackerAccessRightsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var redisKey = $"tracker:permission:{userId:D}";
        try
        {
            var cached = await redisCacheClient.Database.StringGetAsync(redisKey);
            if (cached.HasValue)
            {
                TrackerDiagnostics.CacheHit.Add(1, L2Tag);
                var cachedPermissions = JsonSerializer.Deserialize<TrackerAccessRightsDto>((string)cached!, SerializerOptions);
                return new AccessCacheEnvelope<TrackerAccessRightsDto>(cachedPermissions is not null, cachedPermissions);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis L2 read failed for user permission {UserId}.", userId);
        }

        await using var connection = await postgresConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                p.user_id,
                p.can_leech,
                p.can_seed,
                p.can_scrape,
                p.can_use_private_tracker,
                p.row_version
            from permissions p
            where p.user_id = $1
            limit 1
            """;
        command.Parameters.Add(new NpgsqlParameter { Value = userId });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new AccessCacheEnvelope<TrackerAccessRightsDto>(false, null);
        }

        var permissions = new TrackerAccessRightsDto(
            reader.GetGuid(0),
            reader.GetBoolean(1),
            reader.GetBoolean(2),
            reader.GetBoolean(3),
            reader.GetBoolean(4),
            reader.GetInt64(5));

        try
        {
            await redisCacheClient.Database.StringSetAsync(
                redisKey,
                JsonSerializer.Serialize(permissions, SerializerOptions),
                TimeSpan.FromSeconds(cacheOptions.Value.L2Seconds));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis L2 write failed for user permission {UserId}.", userId);
        }

        return new AccessCacheEnvelope<TrackerAccessRightsDto>(true, permissions);
    }

    public async ValueTask<AccessCacheEnvelope<BanRuleDto>> GetBanRuleAsync(string scope, string subject, CancellationToken cancellationToken)
    {
        var cacheKey = AccessRefreshQueue.ComposeBanRuleKey(scope, subject);
        var redisKey = $"tracker:ban:{cacheKey}";
        try
        {
            var cached = await redisCacheClient.Database.StringGetAsync(redisKey);
            if (cached.HasValue)
            {
                TrackerDiagnostics.CacheHit.Add(1, L2Tag);
                var cachedBan = JsonSerializer.Deserialize<BanRuleDto>((string)cached!, SerializerOptions);
                return new AccessCacheEnvelope<BanRuleDto>(cachedBan is not null, cachedBan);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis L2 read failed for ban rule {Scope}:{Subject}.", scope, subject);
        }

        await using var connection = await postgresConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                b.scope,
                b.subject,
                b.reason,
                b.expires_at_utc,
                b.row_version
            from bans b
            where b.scope = $1
              and b.subject = $2
              and (b.expires_at_utc is null or b.expires_at_utc > now() at time zone 'utc')
            order by b.expires_at_utc nulls first
            limit 1
            """;
        command.Parameters.Add(new NpgsqlParameter { Value = scope });
        command.Parameters.Add(new NpgsqlParameter { Value = subject });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new AccessCacheEnvelope<BanRuleDto>(false, null);
        }

        var banRule = new BanRuleDto(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : new DateTimeOffset(reader.GetFieldValue<DateTime>(3), TimeSpan.Zero),
            reader.GetInt64(4));

        try
        {
            await redisCacheClient.Database.StringSetAsync(
                redisKey,
                JsonSerializer.Serialize(banRule, SerializerOptions),
                TimeSpan.FromSeconds(cacheOptions.Value.L2Seconds));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis L2 write failed for ban rule {Scope}:{Subject}.", scope, subject);
        }

        return new AccessCacheEnvelope<BanRuleDto>(true, banRule);
    }
}

public sealed class HybridAccessSnapshotProvider(
    IMemoryCache memoryCache,
    AccessRefreshQueue refreshQueue,
    IOptions<PolicyCacheOptions> cacheOptions) : IAccessSnapshotProvider
{
    private static readonly KeyValuePair<string, object?> L1Tag = new("layer", "L1");

    public ValueTask<TorrentPolicyDto?> GetTorrentPolicyAsync(string infoHashHex, CancellationToken cancellationToken)
    {
        if (memoryCache.TryGetValue<TorrentPolicyDto>(GetTorrentPolicyKey(infoHashHex), out var policy))
        {
            TrackerDiagnostics.CacheHit.Add(1, L1Tag);
            return ValueTask.FromResult<TorrentPolicyDto?>(policy);
        }

        TrackerDiagnostics.CacheMiss.Add(1);
        refreshQueue.EnqueueTorrentPolicy(infoHashHex);
        return ValueTask.FromResult<TorrentPolicyDto?>(null);
    }

    public ValueTask<PasskeyAccessDto?> GetPasskeyAsync(string passkey, CancellationToken cancellationToken)
    {
        if (memoryCache.TryGetValue<PasskeyAccessDto>(GetPasskeyKey(passkey), out var access))
        {
            TrackerDiagnostics.CacheHit.Add(1, L1Tag);
            return ValueTask.FromResult<PasskeyAccessDto?>(access);
        }

        TrackerDiagnostics.CacheMiss.Add(1);
        refreshQueue.EnqueuePasskey(passkey);
        return ValueTask.FromResult<PasskeyAccessDto?>(null);
    }

    public ValueTask<TrackerAccessRightsDto?> GetTrackerAccessRightsAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (memoryCache.TryGetValue<TrackerAccessRightsDto>(GetUserPermissionKey(userId), out var permissions))
        {
            TrackerDiagnostics.CacheHit.Add(1, L1Tag);
            return ValueTask.FromResult<TrackerAccessRightsDto?>(permissions);
        }

        TrackerDiagnostics.CacheMiss.Add(1);
        refreshQueue.EnqueueUserPermission(userId);
        return ValueTask.FromResult<TrackerAccessRightsDto?>(null);
    }

    public ValueTask<BanRuleDto?> GetBanRuleAsync(string scope, string subject, CancellationToken cancellationToken)
    {
        if (memoryCache.TryGetValue<BanRuleDto>(GetBanRuleKey(scope, subject), out var banRule))
        {
            TrackerDiagnostics.CacheHit.Add(1, L1Tag);
            return ValueTask.FromResult<BanRuleDto?>(banRule);
        }

        TrackerDiagnostics.CacheMiss.Add(1);
        refreshQueue.EnqueueBanRule(scope, subject);
        return ValueTask.FromResult<BanRuleDto?>(null);
    }

    internal void SetTorrentPolicy(TorrentPolicyDto policy)
    {
        memoryCache.Set(GetTorrentPolicyKey(policy.InfoHash), policy, TimeSpan.FromSeconds(cacheOptions.Value.L1Seconds));
    }

    internal void SetPasskey(PasskeyAccessDto access)
    {
        memoryCache.Set(GetPasskeyKey(access.Passkey), access, TimeSpan.FromSeconds(cacheOptions.Value.L1Seconds));
    }

    internal void RemoveTorrentPolicy(string infoHashHex)
    {
        memoryCache.Remove(GetTorrentPolicyKey(infoHashHex));
    }

    internal void RemovePasskey(string passkey)
    {
        memoryCache.Remove(GetPasskeyKey(passkey));
    }

    internal void SetTrackerAccessRights(TrackerAccessRightsDto permissions)
    {
        memoryCache.Set(GetUserPermissionKey(permissions.UserId), permissions, TimeSpan.FromSeconds(cacheOptions.Value.L1Seconds));
    }

    internal void SetBanRule(BanRuleDto banRule)
    {
        memoryCache.Set(GetBanRuleKey(banRule.Scope, banRule.Subject), banRule, TimeSpan.FromSeconds(cacheOptions.Value.L1Seconds));
    }

    internal void RemoveUserPermission(Guid userId)
    {
        memoryCache.Remove(GetUserPermissionKey(userId));
    }

    internal void RemoveBanRule(string scope, string subject)
    {
        memoryCache.Remove(GetBanRuleKey(scope, subject));
    }

    private static string GetTorrentPolicyKey(string infoHashHex) => $"l1:policy:{infoHashHex}";
    private static string GetPasskeyKey(string passkey) => $"l1:passkey:{passkey}";
    private static string GetUserPermissionKey(Guid userId) => $"l1:permission:{userId:D}";
    private static string GetBanRuleKey(string scope, string subject) => $"l1:ban:{scope}:{subject}";
}

internal sealed class AccessSnapshotHydrationService(
    AccessRefreshQueue refreshQueue,
    IAccessSnapshotStore store,
    HybridAccessSnapshotProvider snapshotProvider,
    ILogger<AccessSnapshotHydrationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in refreshQueue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                switch (request.Kind)
                {
                    case AccessRefreshKind.TorrentPolicy:
                    {
                        var envelope = await store.GetTorrentPolicyAsync(request.Key, stoppingToken);
                        if (envelope is { Found: true, Value: not null })
                        {
                            snapshotProvider.SetTorrentPolicy(envelope.Value);
                        }

                        break;
                    }
                    case AccessRefreshKind.Passkey:
                    {
                        var envelope = await store.GetPasskeyAsync(request.Key, stoppingToken);
                        if (envelope is { Found: true, Value: not null })
                        {
                            snapshotProvider.SetPasskey(envelope.Value);
                        }

                        break;
                    }
                    case AccessRefreshKind.UserPermission:
                    {
                        if (!Guid.TryParse(request.Key, out var userId))
                        {
                            break;
                        }

                        var envelope = await store.GetTrackerAccessRightsAsync(userId, stoppingToken);
                        if (envelope is { Found: true, Value: not null })
                        {
                            snapshotProvider.SetTrackerAccessRights(envelope.Value);
                        }

                        break;
                    }
                    case AccessRefreshKind.BanRule:
                    {
                        if (!AccessRefreshQueue.TryParseBanRuleKey(request.Key, out var scope, out var subject))
                        {
                            break;
                        }

                        var envelope = await store.GetBanRuleAsync(scope, subject, stoppingToken);
                        if (envelope is { Found: true, Value: not null })
                        {
                            snapshotProvider.SetBanRule(envelope.Value);
                        }

                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to hydrate access snapshot for {Kind}:{Key}.", request.Kind, request.Key);
            }
            finally
            {
                refreshQueue.Complete(request);
            }
        }
    }
}

public interface IAccessInvalidationPublisher
{
    Task PublishTorrentPolicyInvalidationAsync(string infoHashHex, CancellationToken cancellationToken);
    Task PublishPasskeyInvalidationAsync(string passkey, CancellationToken cancellationToken);
    Task PublishUserPermissionInvalidationAsync(Guid userId, CancellationToken cancellationToken);
    Task PublishBanRuleInvalidationAsync(string scope, string subject, CancellationToken cancellationToken);
}

internal sealed class RedisAccessInvalidationPublisher(IRedisCacheClient redisCacheClient) : IAccessInvalidationPublisher
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private const string ChannelName = "tracker:cache-invalidation";

    public Task PublishTorrentPolicyInvalidationAsync(string infoHashHex, CancellationToken cancellationToken)
        => PublishAsync(new ConfigurationCacheInvalidationMessage(ConfigurationCacheInvalidationKind.TorrentPolicy, infoHashHex));

    public Task PublishPasskeyInvalidationAsync(string passkey, CancellationToken cancellationToken)
        => PublishAsync(new ConfigurationCacheInvalidationMessage(ConfigurationCacheInvalidationKind.Passkey, passkey));

    public Task PublishUserPermissionInvalidationAsync(Guid userId, CancellationToken cancellationToken)
        => PublishAsync(new ConfigurationCacheInvalidationMessage(ConfigurationCacheInvalidationKind.UserPermission, userId.ToString("D")));

    public Task PublishBanRuleInvalidationAsync(string scope, string subject, CancellationToken cancellationToken)
        => PublishAsync(new ConfigurationCacheInvalidationMessage(ConfigurationCacheInvalidationKind.BanRule, AccessRefreshQueue.ComposeBanRuleKey(scope, subject)));

    private Task PublishAsync(ConfigurationCacheInvalidationMessage message)
    {
        return redisCacheClient.Subscriber.PublishAsync(RedisChannel.Literal(ChannelName), JsonSerializer.Serialize(message, SerializerOptions));
    }
}

internal sealed class AccessInvalidationSubscriberService(
    IRedisCacheClient redisCacheClient,
    HybridAccessSnapshotProvider snapshotProvider,
    ILogger<AccessInvalidationSubscriberService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private const string ChannelName = "tracker:cache-invalidation";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await redisCacheClient.Subscriber.SubscribeAsync(RedisChannel.Literal(ChannelName), (_, payload) =>
        {
            try
            {
                var message = JsonSerializer.Deserialize<ConfigurationCacheInvalidationMessage?>((string)payload!, SerializerOptions);
                if (message is null)
                {
                    return;
                }

                switch (message.Value.Kind)
                {
                    case ConfigurationCacheInvalidationKind.TorrentPolicy:
                        snapshotProvider.RemoveTorrentPolicy(message.Value.Key);
                        break;
                    case ConfigurationCacheInvalidationKind.Passkey:
                        snapshotProvider.RemovePasskey(message.Value.Key);
                        break;
                    case ConfigurationCacheInvalidationKind.UserPermission:
                        if (Guid.TryParse(message.Value.Key, out var userId))
                        {
                            snapshotProvider.RemoveUserPermission(userId);
                        }
                        break;
                    case ConfigurationCacheInvalidationKind.BanRule:
                        if (AccessRefreshQueue.TryParseBanRuleKey(message.Value.Key, out var scope, out var subject))
                        {
                            snapshotProvider.RemoveBanRule(scope, subject);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error processing cache invalidation message.");
            }
        });

        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
