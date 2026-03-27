using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using BeeTracker.Caching.Redis;
using BeeTracker.BuildingBlocks.Application.Queries;
using BeeTracker.BuildingBlocks.Infrastructure.Data;
using BeeTracker.Contracts.Admin;
using BeeTracker.Contracts.Configuration;
using BeeTracker.Contracts.Runtime;
using Tracker.AdminService.Application;
using Tracker.ConfigurationService.Application;
using Tracker.ConfigurationService.Infrastructure;

namespace Tracker.AdminService.Infrastructure;

// ─── Cluster Shard Diagnostics Reader ────────────────────────────────────────

public sealed class RedisClusterShardDiagnosticsReader(
    IRedisCacheClient redisCacheClient) : IClusterShardDiagnosticsReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ClusterShardDiagnosticsDto> GetShardDiagnosticsAsync(
        int totalShardCount, CancellationToken cancellationToken)
    {
        var shards = new ClusterShardOwnershipDto[totalShardCount];

        for (var shardId = 0; shardId < totalShardCount; shardId++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = $"cluster:shard:{shardId}:owner";
            var value = await redisCacheClient.Database.StringGetAsync(key);

            string? ownerNodeId = null;
            DateTimeOffset? leaseExpiresAt = null;

            if (value.HasValue)
            {
                var record = JsonSerializer.Deserialize<ShardOwnershipRecord>(value.ToString(), JsonOptions);
                if (record is not null)
                {
                    ownerNodeId = record.OwnerNodeId;
                    leaseExpiresAt = record.LeaseExpiresAtUtc;
                }
            }

            shards[shardId] = new ClusterShardOwnershipDto(shardId, ownerNodeId, LocallyOwned: false, leaseExpiresAt);
        }

        var owned = shards.Count(static s => s.OwnerNodeId is not null);

        return new ClusterShardDiagnosticsDto(
            DateTimeOffset.UtcNow,
            totalShardCount,
            owned,
            totalShardCount - owned,
            shards);
    }
}

// ─── Cluster Node State Reader ────────────────────────────────────────────────

public sealed class RedisClusterNodeStateReader(
    IRedisCacheClient redisCacheClient,
    IConnectionMultiplexer connectionMultiplexer) : IClusterNodeStateReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] HeartbeatKeyPatterns = ["nodes:heartbeat:*", "tracker:nodes:heartbeat:*"];
    private static readonly TimeSpan FreshHeartbeatThreshold = TimeSpan.FromSeconds(45);

    public async Task<IReadOnlyCollection<ClusterNodeStateDto>> GetAllNodeStatesAsync(CancellationToken cancellationToken)
    {
        // Collect heartbeats
        var heartbeats = new Dictionary<string, NodeHeartbeatDto>(StringComparer.Ordinal);
        var endpoints = connectionMultiplexer.GetEndPoints();

        foreach (var endpoint in endpoints)
        {
            var server = connectionMultiplexer.GetServer(endpoint);
            if (!server.IsConnected)
            {
                continue;
            }

            foreach (var pattern in HeartbeatKeyPatterns)
            {
                foreach (var key in server.Keys(pattern: pattern))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var value = await redisCacheClient.Database.StringGetAsync(key);
                    if (!value.HasValue)
                    {
                        continue;
                    }

                    var hb = JsonSerializer.Deserialize<NodeHeartbeatDto>(value.ToString(), JsonOptions);
                    if (hb is not null && !heartbeats.ContainsKey(hb.NodeId))
                    {
                        heartbeats[hb.NodeId] = hb;
                    }
                }
            }
        }

        // Collect operational states
        var opStates = new Dictionary<string, NodeOperationalStateDto>(StringComparer.Ordinal);
        foreach (var endpoint in endpoints)
        {
            var server = connectionMultiplexer.GetServer(endpoint);
            if (!server.IsConnected)
            {
                continue;
            }

            foreach (var key in server.Keys(pattern: "cluster:node:state:*"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var value = await redisCacheClient.Database.StringGetAsync(key);
                if (!value.HasValue)
                {
                    continue;
                }

                var state = JsonSerializer.Deserialize<NodeOperationalStateDto>(value.ToString(), JsonOptions);
                if (state is not null && !opStates.ContainsKey(state.NodeId))
                {
                    opStates[state.NodeId] = state;
                }
            }
        }

        // Collect owned shard counts per node
        var shardCountsPerNode = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var endpoint in endpoints)
        {
            var server = connectionMultiplexer.GetServer(endpoint);
            if (!server.IsConnected)
            {
                continue;
            }

            foreach (var key in server.Keys(pattern: "cluster:shard:*:owner"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var value = await redisCacheClient.Database.StringGetAsync(key);
                if (!value.HasValue)
                {
                    continue;
                }

                var record = JsonSerializer.Deserialize<ShardOwnershipRecord>(value.ToString(), JsonOptions);
                if (record is not null)
                {
                    shardCountsPerNode[record.OwnerNodeId] = shardCountsPerNode.GetValueOrDefault(record.OwnerNodeId) + 1;
                }
            }
        }

        var now = DateTimeOffset.UtcNow;
        var result = new List<ClusterNodeStateDto>(heartbeats.Count);

        foreach (var (nodeId, hb) in heartbeats)
        {
            opStates.TryGetValue(nodeId, out var opState);
            var opStateEnum = opState?.State ?? NodeOperationalState.Active;
            var ownedShards = shardCountsPerNode.GetValueOrDefault(nodeId);
            var heartbeatAge = now - hb.ObservedAtUtc;
            var fresh = heartbeatAge <= FreshHeartbeatThreshold;

            result.Add(new ClusterNodeStateDto(
                nodeId,
                hb.Region,
                opStateEnum.ToString(),
                ownedShards,
                hb.ObservedAtUtc,
                fresh));
        }

        return result.OrderBy(static n => n.NodeId, StringComparer.Ordinal).ToArray();
    }

    public async Task<ClusterNodeStateDto?> GetNodeStateAsync(string nodeId, CancellationToken cancellationToken)
    {
        var all = await GetAllNodeStatesAsync(cancellationToken);
        return all.FirstOrDefault(n => n.NodeId.Equals(nodeId, StringComparison.Ordinal));
    }
}

public sealed class RedisClusterOverviewReader(IRedisCacheClient redisCacheClient, IConnectionMultiplexer connectionMultiplexer) : IClusterOverviewReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] KeyPatterns = ["nodes:heartbeat:*", "tracker:nodes:heartbeat:*"];

    public async Task<ClusterOverviewDto> GetAsync(CancellationToken cancellationToken)
    {
        var nodes = new List<NodeHealthDto>(16);
        var seenNodeIds = new HashSet<string>(StringComparer.Ordinal);
        var endpoints = connectionMultiplexer.GetEndPoints();

        foreach (var endpoint in endpoints)
        {
            var server = connectionMultiplexer.GetServer(endpoint);
            if (!server.IsConnected)
            {
                continue;
            }

            foreach (var pattern in KeyPatterns)
            {
                foreach (var redisKey in server.Keys(pattern: pattern))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var payload = await redisCacheClient.Database.StringGetAsync(redisKey);
                    if (!payload.HasValue)
                    {
                        continue;
                    }

                    var heartbeat = JsonSerializer.Deserialize<NodeHeartbeatDto>(payload.ToString(), JsonOptions);
                    if (heartbeat is null || !seenNodeIds.Add(heartbeat.NodeId))
                    {
                        continue;
                    }

                    nodes.Add(new NodeHealthDto(
                        heartbeat.NodeId,
                        heartbeat.Region,
                        true,
                        heartbeat.ObservedAtUtc));
                }
            }
        }

        return new ClusterOverviewDto(DateTimeOffset.UtcNow, nodes.Count, nodes.OrderBy(static node => node.NodeId, StringComparer.Ordinal).ToArray());
    }
}

public sealed class EfAuditRecordReader(TrackerConfigurationDbContext dbContext) : IAuditRecordReader
{
    public async Task<PageResult<AuditRecordDto>> ListAsync(GridQuery query, CancellationToken cancellationToken)
    {
        query = query.Normalize(defaultPageSize: 25, maxPageSize: 200);
        var normalizedSearch = query.NormalizedSearch;

        var recordsQuery = dbContext.AuditRecords
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var pattern = $"%{normalizedSearch}%";
            recordsQuery = recordsQuery.Where(record =>
                EF.Functions.ILike(record.ActorId, pattern) ||
                EF.Functions.ILike(record.ActorRole, pattern) ||
                EF.Functions.ILike(record.Action, pattern) ||
                EF.Functions.ILike(record.Severity, pattern) ||
                EF.Functions.ILike(record.EntityType, pattern) ||
                EF.Functions.ILike(record.EntityId, pattern) ||
                EF.Functions.ILike(record.CorrelationId, pattern) ||
                (record.RequestId != null && EF.Functions.ILike(record.RequestId, pattern)) ||
                EF.Functions.ILike(record.Result, pattern));
        }

        recordsQuery = query.NormalizedFilter.ToLowerInvariant() switch
        {
            "success" => recordsQuery.Where(record => record.Result == "success"),
            "failure" => recordsQuery.Where(record => record.Result == "failure"),
            "warn" => recordsQuery.Where(record => record.Severity == "warn"),
            "error" => recordsQuery.Where(record => record.Severity == "error"),
            _ => recordsQuery
        };

        var orderedQuery = (query.Sort ?? "occurred:desc").ToLowerInvariant() switch
        {
            "action:asc" => recordsQuery.OrderBy(record => record.Action).ThenByDescending(record => record.OccurredAtUtc),
            "action:desc" => recordsQuery.OrderByDescending(record => record.Action).ThenByDescending(record => record.OccurredAtUtc),
            "severity:asc" => recordsQuery.OrderBy(record => record.Severity).ThenByDescending(record => record.OccurredAtUtc),
            "severity:desc" => recordsQuery.OrderByDescending(record => record.Severity).ThenByDescending(record => record.OccurredAtUtc),
            "actor:asc" => recordsQuery.OrderBy(record => record.ActorId).ThenByDescending(record => record.OccurredAtUtc),
            "actor:desc" => recordsQuery.OrderByDescending(record => record.ActorId).ThenByDescending(record => record.OccurredAtUtc),
            "occurred:asc" => recordsQuery.OrderBy(record => record.OccurredAtUtc),
            _ => recordsQuery.OrderByDescending(record => record.OccurredAtUtc)
        };

        var (records, totalCount) = await orderedQuery.ToPageAsync(query.Page, query.PageSize, cancellationToken);
        var items = records.Select(static record => new AuditRecordDto(
                record.Id,
                new DateTimeOffset(DateTime.SpecifyKind(record.OccurredAtUtc, DateTimeKind.Utc)),
                record.ActorId,
                record.ActorRole,
                record.Action,
                record.Severity,
                record.EntityType,
                record.EntityId,
                record.CorrelationId,
                record.RequestId,
                record.Result,
                record.IpAddress))
            .ToArray()
            .AsReadOnly();

        return new PageResult<AuditRecordDto>(items, totalCount, query.Page, query.PageSize);
    }
}

public sealed class EfMaintenanceRunReader(TrackerConfigurationDbContext dbContext) : IMaintenanceRunReader
{
    public async Task<PageResult<MaintenanceRunDto>> ListAsync(GridQuery query, CancellationToken cancellationToken)
    {
        query = query.Normalize(defaultPageSize: 25, maxPageSize: 200);
        var normalizedSearch = query.NormalizedSearch;

        var runsQuery = dbContext.MaintenanceRuns
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var pattern = $"%{normalizedSearch}%";
            runsQuery = runsQuery.Where(run =>
                EF.Functions.ILike(run.Operation, pattern) ||
                EF.Functions.ILike(run.RequestedBy, pattern) ||
                EF.Functions.ILike(run.Status, pattern) ||
                EF.Functions.ILike(run.CorrelationId, pattern));
        }

        runsQuery = query.NormalizedFilter.ToLowerInvariant() switch
        {
            "completed" => runsQuery.Where(run => run.Status == "completed"),
            "failed" => runsQuery.Where(run => run.Status == "failed"),
            "running" => runsQuery.Where(run => run.Status == "running"),
            _ => runsQuery
        };

        var orderedQuery = (query.Sort ?? "requested:desc").ToLowerInvariant() switch
        {
            "operation:asc" => runsQuery.OrderBy(run => run.Operation).ThenByDescending(run => run.RequestedAtUtc),
            "operation:desc" => runsQuery.OrderByDescending(run => run.Operation).ThenByDescending(run => run.RequestedAtUtc),
            "status:asc" => runsQuery.OrderBy(run => run.Status).ThenByDescending(run => run.RequestedAtUtc),
            "status:desc" => runsQuery.OrderByDescending(run => run.Status).ThenByDescending(run => run.RequestedAtUtc),
            "requested:asc" => runsQuery.OrderBy(run => run.RequestedAtUtc),
            _ => runsQuery.OrderByDescending(run => run.RequestedAtUtc)
        };

        var (runs, totalCount) = await orderedQuery.ToPageAsync(query.Page, query.PageSize, cancellationToken);
        var items = runs.Select(static run => new MaintenanceRunDto(
                run.Id,
                run.Operation,
                run.RequestedBy,
                new DateTimeOffset(DateTime.SpecifyKind(run.RequestedAtUtc, DateTimeKind.Utc)),
                run.Status,
                run.CorrelationId))
            .ToArray()
            .AsReadOnly();

        return new PageResult<MaintenanceRunDto>(items, totalCount, query.Page, query.PageSize);
    }
}

public sealed class EfTorrentAdminReader(TrackerConfigurationDbContext dbContext) : ITorrentAdminReader
{
    public async Task<PageResult<TorrentAdminDto>> ListAsync(GridQuery query, bool? isEnabled, bool? isPrivate, CancellationToken cancellationToken)
    {
        query = query.Normalize(defaultPageSize: 25, maxPageSize: 200);
        var normalizedSearch = query.NormalizedSearch;

        var torrentsQuery = dbContext.Torrents
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var pattern = $"%{normalizedSearch}%";
            torrentsQuery = torrentsQuery.Where(torrent => EF.Functions.ILike(torrent.InfoHash, pattern));
        }

        if (isEnabled.HasValue)
        {
            torrentsQuery = torrentsQuery.Where(torrent => torrent.IsEnabled == isEnabled.Value);
        }

        if (isPrivate.HasValue)
        {
            torrentsQuery = torrentsQuery.Where(torrent => torrent.IsPrivate == isPrivate.Value);
        }

        torrentsQuery = query.NormalizedFilter.ToLowerInvariant() switch
        {
            "enabled" => torrentsQuery.Where(torrent => torrent.IsEnabled),
            "disabled" => torrentsQuery.Where(torrent => !torrent.IsEnabled),
            "private" => torrentsQuery.Where(torrent => torrent.IsPrivate),
            "public" => torrentsQuery.Where(torrent => !torrent.IsPrivate),
            _ => torrentsQuery
        };

        var orderedQuery = (query.Sort ?? "infohash:asc").ToLowerInvariant() switch
        {
            "enabled:asc" => torrentsQuery.OrderBy(torrent => torrent.IsEnabled).ThenBy(torrent => torrent.InfoHash),
            "enabled:desc" => torrentsQuery.OrderByDescending(torrent => torrent.IsEnabled).ThenBy(torrent => torrent.InfoHash),
            "private:asc" => torrentsQuery.OrderBy(torrent => torrent.IsPrivate).ThenBy(torrent => torrent.InfoHash),
            "private:desc" => torrentsQuery.OrderByDescending(torrent => torrent.IsPrivate).ThenBy(torrent => torrent.InfoHash),
            "interval:asc" => torrentsQuery.OrderBy(torrent => torrent.Policy!.AnnounceIntervalSeconds).ThenBy(torrent => torrent.InfoHash),
            "interval:desc" => torrentsQuery.OrderByDescending(torrent => torrent.Policy!.AnnounceIntervalSeconds).ThenBy(torrent => torrent.InfoHash),
            "infohash:desc" => torrentsQuery.OrderByDescending(torrent => torrent.InfoHash),
            _ => torrentsQuery.OrderBy(torrent => torrent.InfoHash)
        };

        var totalCount = await orderedQuery.CountAsync(cancellationToken);
        var torrents = await orderedQuery
            .Include(static torrent => torrent.Policy)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var items = torrents
            .Select(static torrent => MapTorrent(torrent))
            .ToArray()
            .AsReadOnly();

        return new PageResult<TorrentAdminDto>(items, totalCount, query.Page, query.PageSize);
    }

    public async Task<TorrentAdminDto?> GetAsync(string infoHash, CancellationToken cancellationToken)
    {
        var torrent = await dbContext.Torrents
            .AsNoTracking()
            .Include(static torrent => torrent.Policy)
            .Where(torrent => torrent.InfoHash == infoHash.ToUpperInvariant())
            .SingleOrDefaultAsync(cancellationToken);
        return torrent is null ? null : MapTorrent(torrent);
    }

    private static TorrentAdminDto MapTorrent(TorrentConfigurationEntity torrent)
        => new(
            torrent.InfoHash,
            torrent.IsPrivate,
            torrent.IsEnabled,
            torrent.Policy?.AnnounceIntervalSeconds ?? 0,
            torrent.Policy?.MinAnnounceIntervalSeconds ?? 0,
            torrent.Policy?.DefaultNumWant ?? 0,
            torrent.Policy?.MaxNumWant ?? 0,
            torrent.Policy?.AllowScrape ?? false,
            torrent.Policy?.RowVersion ?? 0);
}

public sealed class EfPasskeyAdminReader(TrackerConfigurationDbContext dbContext) : IPasskeyAdminReader
{
    public async Task<PageResult<PasskeyAdminDto>> ListAsync(GridQuery query, Guid? userId, bool? isRevoked, CancellationToken cancellationToken)
    {
        query = query.Normalize(defaultPageSize: 25, maxPageSize: 200);
        var normalizedSearch = query.NormalizedSearch;

        var passkeysQuery = dbContext.Passkeys
            .AsNoTracking()
            .AsQueryable();

        if (userId.HasValue)
        {
            passkeysQuery = passkeysQuery.Where(passkey => passkey.UserId == userId.Value);
        }

        if (isRevoked.HasValue)
        {
            passkeysQuery = passkeysQuery.Where(passkey => passkey.IsRevoked == isRevoked.Value);
        }

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var pattern = $"%{normalizedSearch}%";
            passkeysQuery = passkeysQuery.Where(passkey =>
                EF.Functions.ILike(passkey.UserId.ToString(), pattern) ||
                EF.Functions.ILike(passkey.Passkey, pattern));
        }

        passkeysQuery = query.NormalizedFilter.ToLowerInvariant() switch
        {
            "revoked" => passkeysQuery.Where(passkey => passkey.IsRevoked),
            "active" => passkeysQuery.Where(passkey => !passkey.IsRevoked),
            "expired" => passkeysQuery.Where(passkey => passkey.ExpiresAtUtc.HasValue && passkey.ExpiresAtUtc < DateTime.UtcNow),
            _ => passkeysQuery
        };

        var orderedQuery = (query.Sort ?? "userid:asc").ToLowerInvariant() switch
        {
            "expires:asc" => passkeysQuery.OrderBy(passkey => passkey.ExpiresAtUtc ?? DateTime.MaxValue).ThenBy(passkey => passkey.UserId),
            "expires:desc" => passkeysQuery.OrderByDescending(passkey => passkey.ExpiresAtUtc ?? DateTime.MaxValue).ThenBy(passkey => passkey.UserId),
            "version:asc" => passkeysQuery.OrderBy(passkey => passkey.RowVersion).ThenBy(passkey => passkey.UserId),
            "version:desc" => passkeysQuery.OrderByDescending(passkey => passkey.RowVersion).ThenBy(passkey => passkey.UserId),
            "userid:desc" => passkeysQuery.OrderByDescending(passkey => passkey.UserId).ThenByDescending(passkey => passkey.Passkey),
            _ => passkeysQuery.OrderBy(passkey => passkey.UserId).ThenBy(passkey => passkey.Passkey)
        };

        var (passkeys, totalCount) = await orderedQuery.ToPageAsync(query.Page, query.PageSize, cancellationToken);

        return new PageResult<PasskeyAdminDto>(passkeys.Select(static passkey => new PasskeyAdminDto(
                MaskPasskey(passkey.Passkey),
                passkey.UserId,
                passkey.IsRevoked,
                passkey.ExpiresAtUtc.HasValue
                    ? new DateTimeOffset(DateTime.SpecifyKind(passkey.ExpiresAtUtc.Value, DateTimeKind.Utc))
                    : null,
                passkey.RowVersion))
            .ToArray()
            .AsReadOnly(), totalCount, query.Page, query.PageSize);
    }

    private static string MaskPasskey(string passkey)
        => passkey.Length <= 6 ? $"pk:{passkey[0]}...{passkey[^1]}" : $"pk:{passkey[..4]}...{passkey[^2..]}";
}

public sealed class EfTrackerAccessRightsAdminReader(TrackerConfigurationDbContext dbContext) : ITrackerAccessRightsAdminReader
{
    public async Task<PageResult<TrackerAccessAdminDto>> ListAsync(GridQuery query, bool? canUsePrivateTracker, CancellationToken cancellationToken)
    {
        query = query.Normalize(defaultPageSize: 25, maxPageSize: 200);
        var normalizedSearch = query.NormalizedSearch;

        var permissionsQuery = dbContext.Permissions
            .AsNoTracking()
            .AsQueryable();

        if (canUsePrivateTracker.HasValue)
        {
            permissionsQuery = permissionsQuery.Where(permission => permission.CanUsePrivateTracker == canUsePrivateTracker.Value);
        }

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var pattern = $"%{normalizedSearch}%";
            permissionsQuery = permissionsQuery.Where(permission =>
                EF.Functions.ILike(permission.UserId.ToString(), pattern));
        }

        permissionsQuery = query.NormalizedFilter.ToLowerInvariant() switch
        {
            "private" => permissionsQuery.Where(permission => permission.CanUsePrivateTracker),
            "public" => permissionsQuery.Where(permission => !permission.CanUsePrivateTracker),
            "seed" => permissionsQuery.Where(permission => permission.CanSeed),
            "leech" => permissionsQuery.Where(permission => permission.CanLeech),
            "scrape" => permissionsQuery.Where(permission => permission.CanScrape),
            _ => permissionsQuery
        };

        IOrderedQueryable<UserPermissionEntity>? orderedQuery = null;
        foreach (var term in GridQuerySortParser.Parse(query.Sort))
        {
            orderedQuery = ApplyTrackerAccessSort(orderedQuery ?? permissionsQuery, term);
        }

        orderedQuery ??= permissionsQuery
            .OrderBy(permission => permission.UserId);

        var (permissions, totalCount) = await orderedQuery.ToPageAsync(query.Page, query.PageSize, cancellationToken);

        return new PageResult<TrackerAccessAdminDto>(permissions
            .Select(static permission => new TrackerAccessAdminDto(
                permission.UserId,
                permission.CanLeech,
                permission.CanSeed,
                permission.CanScrape,
                permission.CanUsePrivateTracker,
                permission.RowVersion))
            .ToArray()
            .AsReadOnly(), totalCount, query.Page, query.PageSize);
    }

    private static IOrderedQueryable<UserPermissionEntity> ApplyTrackerAccessSort(
        IQueryable<UserPermissionEntity> source,
        GridSortTerm term)
        => (term.Field.ToLowerInvariant(), term.Direction) switch
        {
            ("private", GridSortDirection.Asc) => source.OrderBy(permission => permission.CanUsePrivateTracker).ThenBy(permission => permission.UserId),
            ("private", GridSortDirection.Desc) => source.OrderByDescending(permission => permission.CanUsePrivateTracker).ThenBy(permission => permission.UserId),
            ("seed", GridSortDirection.Asc) => source.OrderBy(permission => permission.CanSeed).ThenBy(permission => permission.UserId),
            ("seed", GridSortDirection.Desc) => source.OrderByDescending(permission => permission.CanSeed).ThenBy(permission => permission.UserId),
            ("leech", GridSortDirection.Asc) => source.OrderBy(permission => permission.CanLeech).ThenBy(permission => permission.UserId),
            ("leech", GridSortDirection.Desc) => source.OrderByDescending(permission => permission.CanLeech).ThenBy(permission => permission.UserId),
            ("scrape", GridSortDirection.Asc) => source.OrderBy(permission => permission.CanScrape).ThenBy(permission => permission.UserId),
            ("scrape", GridSortDirection.Desc) => source.OrderByDescending(permission => permission.CanScrape).ThenBy(permission => permission.UserId),
            ("version", GridSortDirection.Asc) => source.OrderBy(permission => permission.RowVersion).ThenBy(permission => permission.UserId),
            ("version", GridSortDirection.Desc) => source.OrderByDescending(permission => permission.RowVersion).ThenBy(permission => permission.UserId),
            ("userid", GridSortDirection.Desc) => source.OrderByDescending(permission => permission.UserId),
            _ => source.OrderBy(permission => permission.UserId)
        };
}

public sealed class EfBanAdminReader(TrackerConfigurationDbContext dbContext) : IBanAdminReader
{
    public async Task<PageResult<BanRuleAdminDto>> ListAsync(GridQuery query, string? scope, CancellationToken cancellationToken)
    {
        query = query.Normalize(defaultPageSize: 25, maxPageSize: 200);
        var normalizedSearch = query.NormalizedSearch;

        var bansQuery = dbContext.Bans
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(scope))
        {
            bansQuery = bansQuery.Where(ban => ban.Scope == scope);
        }

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var pattern = $"%{normalizedSearch}%";
            bansQuery = bansQuery.Where(ban =>
                EF.Functions.ILike(ban.Scope, pattern) ||
                EF.Functions.ILike(ban.Subject, pattern) ||
                EF.Functions.ILike(ban.Reason, pattern));
        }

        bansQuery = query.NormalizedFilter.ToLowerInvariant() switch
        {
            "active" => bansQuery.Where(ban => !ban.ExpiresAtUtc.HasValue || ban.ExpiresAtUtc > DateTime.UtcNow),
            "expired" => bansQuery.Where(ban => ban.ExpiresAtUtc.HasValue && ban.ExpiresAtUtc <= DateTime.UtcNow),
            _ => bansQuery
        };

        IOrderedQueryable<BanRuleEntity>? orderedQuery = null;
        foreach (var term in GridQuerySortParser.Parse(query.Sort))
        {
            orderedQuery = ApplyBanSort(orderedQuery ?? bansQuery, term);
        }

        orderedQuery ??= bansQuery
            .OrderBy(ban => ban.Scope)
            .ThenBy(ban => ban.Subject);

        var (bans, totalCount) = await orderedQuery.ToPageAsync(query.Page, query.PageSize, cancellationToken);

        return new PageResult<BanRuleAdminDto>(bans
            .Select(static ban => new BanRuleAdminDto(
                ban.Scope,
                ban.Subject,
                ban.Reason,
                ban.ExpiresAtUtc.HasValue
                    ? new DateTimeOffset(DateTime.SpecifyKind(ban.ExpiresAtUtc.Value, DateTimeKind.Utc))
                    : null,
                ban.RowVersion))
            .ToArray()
            .AsReadOnly(), totalCount, query.Page, query.PageSize);
    }

    private static IOrderedQueryable<BanRuleEntity> ApplyBanSort(
        IQueryable<BanRuleEntity> source,
        GridSortTerm term)
        => (term.Field.ToLowerInvariant(), term.Direction) switch
        {
            ("subject", GridSortDirection.Asc) => source.OrderBy(ban => ban.Subject).ThenBy(ban => ban.Scope),
            ("subject", GridSortDirection.Desc) => source.OrderByDescending(ban => ban.Subject).ThenBy(ban => ban.Scope),
            ("expires", GridSortDirection.Asc) => source.OrderBy(ban => ban.ExpiresAtUtc ?? DateTime.MaxValue).ThenBy(ban => ban.Scope).ThenBy(ban => ban.Subject),
            ("expires", GridSortDirection.Desc) => source.OrderByDescending(ban => ban.ExpiresAtUtc ?? DateTime.MaxValue).ThenBy(ban => ban.Scope).ThenBy(ban => ban.Subject),
            ("scope", GridSortDirection.Desc) => source.OrderByDescending(ban => ban.Scope).ThenBy(ban => ban.Subject),
            _ => source.OrderBy(ban => ban.Scope).ThenBy(ban => ban.Subject)
        };
}

#pragma warning disable CS0618
public sealed class ConfigurationMutationOrchestrator(
    IConfigurationMutationService configurationMutationService,
    IConfigurationMutationPreviewService configurationMutationPreviewService,
    IConfigurationMaintenanceService configurationMaintenanceService) : IAdminMutationOrchestrator
{
    private static string MaskPasskey(string passkey)
        => passkey.Length <= 6 ? $"pk:{passkey[0]}...{passkey[^1]}" : $"pk:{passkey[..4]}...{passkey[^2..]}";

    private static BulkPasskeyOperationItemDto CreatePasskeySuccessItem(PasskeyAccessDto snapshot, string? newPasskey = null, string? newPasskeyMask = null)
        => new(
            MaskPasskey(snapshot.Passkey),
            true,
            null,
            null,
            new PasskeyAdminDto(
                MaskPasskey(snapshot.Passkey),
                snapshot.UserId,
                snapshot.IsRevoked,
                snapshot.ExpiresAtUtc,
                snapshot.Version),
            newPasskey,
            newPasskeyMask);

    private static BulkPasskeyOperationItemDto CreatePasskeyFailureItem(string passkey, string errorCode, string message)
        => new(MaskPasskey(passkey), false, errorCode, message, null, null, null);

    private static BulkBanOperationItemDto CreateBanSuccessItem(BanRuleDto snapshot)
        => new(
            snapshot.Scope,
            snapshot.Subject,
            true,
            null,
            null,
            new BanRuleAdminDto(
                snapshot.Scope,
                snapshot.Subject,
                snapshot.Reason,
                snapshot.ExpiresAtUtc,
                snapshot.Version));

    private static BulkBanOperationItemDto CreateBanFailureItem(string scope, string subject, string errorCode, string message)
        => new(scope, subject, false, errorCode, message, null);

    private static BulkTorrentOperationItemDto CreateTorrentSuccessItem(TorrentPolicyDto snapshot)
        => new(
            snapshot.InfoHash,
            true,
            null,
            null,
            new TorrentAdminDto(
                snapshot.InfoHash,
                snapshot.IsPrivate,
                snapshot.IsEnabled,
                snapshot.AnnounceIntervalSeconds,
                snapshot.MinAnnounceIntervalSeconds,
                snapshot.DefaultNumWant,
                snapshot.MaxNumWant,
                snapshot.AllowScrape,
                snapshot.Version));

    private static BulkTorrentOperationItemDto CreateTorrentFailureItem(string infoHash, string errorCode, string message)
        => new(infoHash.ToUpperInvariant(), false, errorCode, message, null);

    private static TorrentPolicyDryRunItemDto CreateTorrentPolicyDryRunItem(TorrentPolicyMutationPreviewDto preview)
        => new(
            preview.ProposedSnapshot.InfoHash,
            preview.CanApply,
            preview.ErrorCode,
            preview.ErrorMessage,
            preview.CurrentSnapshot is null
                ? null
                : new TorrentAdminDto(
                    preview.CurrentSnapshot.InfoHash,
                    preview.CurrentSnapshot.IsPrivate,
                    preview.CurrentSnapshot.IsEnabled,
                    preview.CurrentSnapshot.AnnounceIntervalSeconds,
                    preview.CurrentSnapshot.MinAnnounceIntervalSeconds,
                    preview.CurrentSnapshot.DefaultNumWant,
                    preview.CurrentSnapshot.MaxNumWant,
                    preview.CurrentSnapshot.AllowScrape,
                    preview.CurrentSnapshot.Version),
            new TorrentAdminDto(
                preview.ProposedSnapshot.InfoHash,
                preview.ProposedSnapshot.IsPrivate,
                preview.ProposedSnapshot.IsEnabled,
                preview.ProposedSnapshot.AnnounceIntervalSeconds,
                preview.ProposedSnapshot.MinAnnounceIntervalSeconds,
                preview.ProposedSnapshot.DefaultNumWant,
                preview.ProposedSnapshot.MaxNumWant,
                preview.ProposedSnapshot.AllowScrape,
                preview.ProposedSnapshot.Version),
            preview.Warnings);

    public Task<TorrentPolicyDto> UpsertTorrentPolicyAsync(string infoHash, TorrentPolicyUpsertRequest request, AdminMutationContext context, CancellationToken cancellationToken)
        => configurationMutationService.UpsertTorrentPolicyAsync(infoHash, request, context, cancellationToken);

    public async Task<BulkOperationResultDto> BulkActivateTorrentsAsync(IReadOnlyCollection<BulkTorrentActivationItem> items, AdminMutationContext context, CancellationToken cancellationToken)
    {
        var results = new List<BulkTorrentOperationItemDto>(items.Count);

        foreach (var item in items)
        {
            try
            {
                var snapshot = await configurationMutationService.ActivateTorrentAsync(
                    item.InfoHash,
                    new TorrentActivationRequest(item.ExpectedVersion),
                    context,
                    cancellationToken);

                results.Add(CreateTorrentSuccessItem(snapshot));
            }
            catch (ConfigurationConcurrencyException exception)
            {
                results.Add(CreateTorrentFailureItem(item.InfoHash, "concurrency_conflict", exception.Message));
            }
            catch (ConfigurationEntityNotFoundException exception)
            {
                results.Add(CreateTorrentFailureItem(item.InfoHash, "not_found", exception.Message));
            }
        }

        return new BulkOperationResultDto(
            results.Count,
            results.Count(static item => item.Succeeded),
            results.Count(static item => !item.Succeeded),
            Array.Empty<BulkPasskeyOperationItemDto>(),
            results,
            Array.Empty<BulkUserPermissionOperationItemDto>(),
            Array.Empty<BulkBanOperationItemDto>());
    }

    public async Task<BulkOperationResultDto> BulkDeactivateTorrentsAsync(IReadOnlyCollection<BulkTorrentActivationItem> items, AdminMutationContext context, CancellationToken cancellationToken)
    {
        var results = new List<BulkTorrentOperationItemDto>(items.Count);

        foreach (var item in items)
        {
            try
            {
                var snapshot = await configurationMutationService.DeactivateTorrentAsync(
                    item.InfoHash,
                    new TorrentActivationRequest(item.ExpectedVersion),
                    context,
                    cancellationToken);

                results.Add(CreateTorrentSuccessItem(snapshot));
            }
            catch (ConfigurationConcurrencyException exception)
            {
                results.Add(CreateTorrentFailureItem(item.InfoHash, "concurrency_conflict", exception.Message));
            }
            catch (ConfigurationEntityNotFoundException exception)
            {
                results.Add(CreateTorrentFailureItem(item.InfoHash, "not_found", exception.Message));
            }
        }

        return new BulkOperationResultDto(
            results.Count,
            results.Count(static item => item.Succeeded),
            results.Count(static item => !item.Succeeded),
            Array.Empty<BulkPasskeyOperationItemDto>(),
            results,
            Array.Empty<BulkUserPermissionOperationItemDto>(),
            Array.Empty<BulkBanOperationItemDto>());
    }

    public async Task<BulkOperationResultDto> BulkUpsertTorrentPoliciesAsync(IReadOnlyCollection<BulkTorrentPolicyUpsertItem> items, AdminMutationContext context, CancellationToken cancellationToken)
    {
        var results = new List<BulkTorrentOperationItemDto>(items.Count);

        foreach (var item in items)
        {
            try
            {
                var snapshot = await configurationMutationService.UpsertTorrentPolicyAsync(
                    item.InfoHash,
                    new TorrentPolicyUpsertRequest(
                        item.IsPrivate,
                        item.IsEnabled,
                        item.AnnounceIntervalSeconds,
                        item.MinAnnounceIntervalSeconds,
                        item.DefaultNumWant,
                        item.MaxNumWant,
                        item.AllowScrape,
                        item.WarningMessage,
                        item.ExpectedVersion),
                    context,
                    cancellationToken);

                results.Add(CreateTorrentSuccessItem(snapshot));
            }
            catch (ConfigurationConcurrencyException exception)
            {
                results.Add(CreateTorrentFailureItem(item.InfoHash, "concurrency_conflict", exception.Message));
            }
            catch (ConfigurationEntityNotFoundException exception)
            {
                results.Add(CreateTorrentFailureItem(item.InfoHash, "not_found", exception.Message));
            }
        }

        return new BulkOperationResultDto(
            results.Count,
            results.Count(static item => item.Succeeded),
            results.Count(static item => !item.Succeeded),
            Array.Empty<BulkPasskeyOperationItemDto>(),
            results,
            Array.Empty<BulkUserPermissionOperationItemDto>(),
            Array.Empty<BulkBanOperationItemDto>());
    }

    public async Task<BulkDryRunResultDto> DryRunBulkUpsertTorrentPoliciesAsync(IReadOnlyCollection<BulkTorrentPolicyUpsertItem> items, CancellationToken cancellationToken)
    {
        var results = new List<TorrentPolicyDryRunItemDto>(items.Count);

        foreach (var item in items)
        {
            var preview = await configurationMutationPreviewService.PreviewUpsertTorrentPolicyAsync(
                item.InfoHash,
                new TorrentPolicyUpsertRequest(
                    item.IsPrivate,
                    item.IsEnabled,
                    item.AnnounceIntervalSeconds,
                    item.MinAnnounceIntervalSeconds,
                    item.DefaultNumWant,
                    item.MaxNumWant,
                    item.AllowScrape,
                    item.WarningMessage,
                    item.ExpectedVersion),
                cancellationToken);

            results.Add(CreateTorrentPolicyDryRunItem(preview));
        }

        return new BulkDryRunResultDto(
            results.Count,
            results.Count(static item => item.CanApply),
            results.Count(static item => !item.CanApply),
            results);
    }

    public Task<PasskeyAccessDto> UpsertPasskeyAsync(string passkey, PasskeyUpsertRequest request, AdminMutationContext context, CancellationToken cancellationToken)
        => configurationMutationService.UpsertPasskeyAsync(passkey, request, context, cancellationToken);

    public async Task<BulkOperationResultDto> BulkRevokePasskeysAsync(IReadOnlyCollection<BulkPasskeyRevokeItem> items, AdminMutationContext context, CancellationToken cancellationToken)
    {
        var results = new List<BulkPasskeyOperationItemDto>(items.Count);

        foreach (var item in items)
        {
            try
            {
                var snapshot = await configurationMutationService.RevokePasskeyAsync(
                    item.Passkey,
                    new PasskeyRevokeRequest(item.ExpectedVersion),
                    context,
                    cancellationToken);

                results.Add(CreatePasskeySuccessItem(snapshot));
            }
            catch (ConfigurationConcurrencyException exception)
            {
                results.Add(CreatePasskeyFailureItem(item.Passkey, "concurrency_conflict", exception.Message));
            }
            catch (ConfigurationEntityNotFoundException exception)
            {
                results.Add(CreatePasskeyFailureItem(item.Passkey, "not_found", exception.Message));
            }
        }

        return new BulkOperationResultDto(
            results.Count,
            results.Count(static item => item.Succeeded),
            results.Count(static item => !item.Succeeded),
            results,
            Array.Empty<BulkTorrentOperationItemDto>(),
            Array.Empty<BulkUserPermissionOperationItemDto>(),
            Array.Empty<BulkBanOperationItemDto>());
    }

    public async Task<BulkOperationResultDto> BulkRotatePasskeysAsync(IReadOnlyCollection<BulkPasskeyRotateItem> items, AdminMutationContext context, CancellationToken cancellationToken)
    {
        var results = new List<BulkPasskeyOperationItemDto>(items.Count);

        foreach (var item in items)
        {
            try
            {
                var (revokedSnapshot, newSnapshot) = await configurationMutationService.RotatePasskeyAsync(
                    item.Passkey,
                    new PasskeyRotateRequest(item.ExpiresAtUtc, item.ExpectedVersion),
                    context,
                    cancellationToken);

                results.Add(CreatePasskeySuccessItem(
                    revokedSnapshot,
                    newSnapshot.Passkey,
                    MaskPasskey(newSnapshot.Passkey)));
            }
            catch (ConfigurationConcurrencyException exception)
            {
                results.Add(CreatePasskeyFailureItem(item.Passkey, "concurrency_conflict", exception.Message));
            }
            catch (ConfigurationEntityNotFoundException exception)
            {
                results.Add(CreatePasskeyFailureItem(item.Passkey, "not_found", exception.Message));
            }
        }

        return new BulkOperationResultDto(
            results.Count,
            results.Count(static item => item.Succeeded),
            results.Count(static item => !item.Succeeded),
            results,
            Array.Empty<BulkTorrentOperationItemDto>(),
            Array.Empty<BulkUserPermissionOperationItemDto>(),
            Array.Empty<BulkBanOperationItemDto>());
    }

    public Task<TrackerAccessRightsDto> UpsertTrackerAccessRightsAsync(Guid userId, TrackerAccessRightsUpsertRequest request, AdminMutationContext context, CancellationToken cancellationToken)
    {
        return configurationMutationService.UpsertTrackerAccessRightsAsync(
            userId,
            request,
            context,
            cancellationToken);
    }

    public async Task<BulkOperationResultDto> BulkUpsertTrackerAccessRightsAsync(IReadOnlyCollection<BulkTrackerAccessRightsUpsertItem> items, AdminMutationContext context, CancellationToken cancellationToken)
    {
        var results = new List<BulkTrackerAccessOperationItemDto>(items.Count);

        foreach (var item in items)
        {
            try
            {
                var snapshot = await configurationMutationService.UpsertTrackerAccessRightsAsync(
                    item.UserId,
                    new TrackerAccessRightsUpsertRequest(item.CanLeech, item.CanSeed, item.CanScrape, item.CanUsePrivateTracker, item.ExpectedVersion),
                    context,
                    cancellationToken);

                results.Add(new BulkTrackerAccessOperationItemDto(
                    item.UserId,
                    true,
                    null,
                    null,
                    new TrackerAccessAdminDto(
                        snapshot.UserId,
                        snapshot.CanLeech,
                        snapshot.CanSeed,
                        snapshot.CanScrape,
                        snapshot.CanUsePrivateTracker,
                        snapshot.Version)));
            }
            catch (ConfigurationConcurrencyException exception)
            {
                results.Add(new BulkTrackerAccessOperationItemDto(item.UserId, false, "concurrency_conflict", exception.Message, null));
            }
        }

        return new BulkOperationResultDto(
            results.Count,
            results.Count(static item => item.Succeeded),
            results.Count(static item => !item.Succeeded),
            Array.Empty<BulkPasskeyOperationItemDto>(),
            Array.Empty<BulkTorrentOperationItemDto>(),
            results.Select(static item => item.ToUserPermissionOperationItem()).ToArray(),
            Array.Empty<BulkBanOperationItemDto>(),
            results);
    }

    public Task<BanRuleDto> UpsertBanRuleAsync(string scope, string subject, BanRuleUpsertRequest request, AdminMutationContext context, CancellationToken cancellationToken)
        => configurationMutationService.UpsertBanRuleAsync(scope, subject, request, context, cancellationToken);

    public async Task<BulkOperationResultDto> BulkUpsertBanRulesAsync(IReadOnlyCollection<BulkBanRuleUpsertItem> items, AdminMutationContext context, CancellationToken cancellationToken)
    {
        var results = new List<BulkBanOperationItemDto>(items.Count);

        foreach (var item in items)
        {
            try
            {
                var snapshot = await configurationMutationService.UpsertBanRuleAsync(
                    item.Scope,
                    item.Subject,
                    new BanRuleUpsertRequest(item.Reason, item.ExpiresAtUtc, item.ExpectedVersion),
                    context,
                    cancellationToken);

                results.Add(CreateBanSuccessItem(snapshot));
            }
            catch (ConfigurationConcurrencyException exception)
            {
                results.Add(CreateBanFailureItem(item.Scope, item.Subject, "concurrency_conflict", exception.Message));
            }
            catch (ConfigurationEntityNotFoundException exception)
            {
                results.Add(CreateBanFailureItem(item.Scope, item.Subject, "not_found", exception.Message));
            }
        }

        return new BulkOperationResultDto(
            results.Count,
            results.Count(static item => item.Succeeded),
            results.Count(static item => !item.Succeeded),
            Array.Empty<BulkPasskeyOperationItemDto>(),
            Array.Empty<BulkTorrentOperationItemDto>(),
            Array.Empty<BulkUserPermissionOperationItemDto>(),
            results);
    }

    public async Task<BulkOperationResultDto> BulkExpireBanRulesAsync(IReadOnlyCollection<BulkBanRuleExpireItem> items, AdminMutationContext context, CancellationToken cancellationToken)
    {
        var results = new List<BulkBanOperationItemDto>(items.Count);

        foreach (var item in items)
        {
            try
            {
                var snapshot = await configurationMutationService.ExpireBanRuleAsync(
                    item.Scope,
                    item.Subject,
                    new BanRuleExpireRequest(item.ExpiresAtUtc, item.ExpectedVersion),
                    context,
                    cancellationToken);

                results.Add(CreateBanSuccessItem(snapshot));
            }
            catch (ConfigurationConcurrencyException exception)
            {
                results.Add(CreateBanFailureItem(item.Scope, item.Subject, "concurrency_conflict", exception.Message));
            }
            catch (ConfigurationEntityNotFoundException exception)
            {
                results.Add(CreateBanFailureItem(item.Scope, item.Subject, "not_found", exception.Message));
            }
        }

        return new BulkOperationResultDto(
            results.Count,
            results.Count(static item => item.Succeeded),
            results.Count(static item => !item.Succeeded),
            Array.Empty<BulkPasskeyOperationItemDto>(),
            Array.Empty<BulkTorrentOperationItemDto>(),
            Array.Empty<BulkUserPermissionOperationItemDto>(),
            results);
    }

    public async Task<BulkOperationResultDto> BulkDeleteBanRulesAsync(IReadOnlyCollection<BulkBanRuleDeleteItem> items, AdminMutationContext context, CancellationToken cancellationToken)
    {
        var results = new List<BulkBanOperationItemDto>(items.Count);

        foreach (var item in items)
        {
            try
            {
                await configurationMutationService.DeleteBanRuleAsync(
                    item.Scope,
                    item.Subject,
                    item.ExpectedVersion,
                    context,
                    cancellationToken);

                results.Add(new BulkBanOperationItemDto(
                    item.Scope,
                    item.Subject,
                    true,
                    null,
                    null,
                    null));
            }
            catch (ConfigurationConcurrencyException exception)
            {
                results.Add(CreateBanFailureItem(item.Scope, item.Subject, "concurrency_conflict", exception.Message));
            }
            catch (ConfigurationEntityNotFoundException exception)
            {
                results.Add(CreateBanFailureItem(item.Scope, item.Subject, "not_found", exception.Message));
            }
        }

        return new BulkOperationResultDto(
            results.Count,
            results.Count(static item => item.Succeeded),
            results.Count(static item => !item.Succeeded),
            Array.Empty<BulkPasskeyOperationItemDto>(),
            Array.Empty<BulkTorrentOperationItemDto>(),
            Array.Empty<BulkUserPermissionOperationItemDto>(),
            results);
    }

    public Task DeleteBanRuleAsync(string scope, string subject, long? expectedVersion, AdminMutationContext context, CancellationToken cancellationToken)
        => configurationMutationService.DeleteBanRuleAsync(scope, subject, expectedVersion, context, cancellationToken);

    public Task TriggerCacheRefreshAsync(string operation, AdminMutationContext context, CancellationToken cancellationToken)
        => configurationMaintenanceService.TriggerCacheRefreshAsync(operation, context, cancellationToken);
}

#pragma warning restore CS0618
public static class AdminInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddAdminInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddConfigurationInfrastructure(configuration);
        services.AddScoped<IClusterOverviewReader, RedisClusterOverviewReader>();
        services.AddScoped<IClusterShardDiagnosticsReader, RedisClusterShardDiagnosticsReader>();
        services.AddScoped<IClusterNodeStateReader, RedisClusterNodeStateReader>();
        services.AddScoped<IAuditRecordReader, EfAuditRecordReader>();
        services.AddScoped<IMaintenanceRunReader, EfMaintenanceRunReader>();
        services.AddScoped<ITorrentAdminReader, EfTorrentAdminReader>();
        services.AddScoped<IPasskeyAdminReader, EfPasskeyAdminReader>();
        services.AddScoped<ITrackerAccessRightsAdminReader, EfTrackerAccessRightsAdminReader>();
        services.AddScoped<IBanAdminReader, EfBanAdminReader>();
        services.AddScoped<IAdminMutationOrchestrator, ConfigurationMutationOrchestrator>();
        return services;
    }
}
