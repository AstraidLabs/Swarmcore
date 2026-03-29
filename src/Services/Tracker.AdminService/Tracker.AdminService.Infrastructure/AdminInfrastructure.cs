using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
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
    public async Task<PageResult<AuditRecordDto>> ListAsync(GridQuery query, AuditRecordFilter filter, CancellationToken cancellationToken)
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

        recordsQuery = filter switch
        {
            AuditRecordFilter.Success => recordsQuery.Where(record => record.Result == "success"),
            AuditRecordFilter.Failure => recordsQuery.Where(record => record.Result == "failure"),
            AuditRecordFilter.Warn => recordsQuery.Where(record => record.Severity == "warn"),
            _ => recordsQuery
        };

        IOrderedQueryable<AuditRecordEntity>? orderedQuery = null;
        foreach (var term in AdminCatalogProfiles.Audit.ParseSort(query.Sort))
        {
            orderedQuery = ApplyAuditSort(orderedQuery ?? recordsQuery, term);
        }

        orderedQuery ??= recordsQuery.OrderByDescending(record => record.OccurredAtUtc);

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

    private static IOrderedQueryable<AuditRecordEntity> ApplyAuditSort(
        IQueryable<AuditRecordEntity> source,
        GridSortTerm term)
        => (term.Field.ToLowerInvariant(), term.Direction) switch
        {
            ("action", GridSortDirection.Asc) => source.OrderBy(record => record.Action).ThenByDescending(record => record.OccurredAtUtc),
            ("action", GridSortDirection.Desc) => source.OrderByDescending(record => record.Action).ThenByDescending(record => record.OccurredAtUtc),
            ("severity", GridSortDirection.Asc) => source.OrderBy(record => record.Severity).ThenByDescending(record => record.OccurredAtUtc),
            ("severity", GridSortDirection.Desc) => source.OrderByDescending(record => record.Severity).ThenByDescending(record => record.OccurredAtUtc),
            ("actor", GridSortDirection.Asc) => source.OrderBy(record => record.ActorId).ThenByDescending(record => record.OccurredAtUtc),
            ("actor", GridSortDirection.Desc) => source.OrderByDescending(record => record.ActorId).ThenByDescending(record => record.OccurredAtUtc),
            ("occurred", GridSortDirection.Asc) => source.OrderBy(record => record.OccurredAtUtc),
            _ => source.OrderByDescending(record => record.OccurredAtUtc)
        };
}

public sealed class EfMaintenanceRunReader(TrackerConfigurationDbContext dbContext) : IMaintenanceRunReader
{
    public async Task<PageResult<MaintenanceRunDto>> ListAsync(GridQuery query, MaintenanceRunFilter filter, CancellationToken cancellationToken)
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

        runsQuery = filter switch
        {
            MaintenanceRunFilter.Completed => runsQuery.Where(run => run.Status == "completed"),
            MaintenanceRunFilter.Failed => runsQuery.Where(run => run.Status == "failed"),
            MaintenanceRunFilter.Running => runsQuery.Where(run => run.Status == "running"),
            _ => runsQuery
        };

        IOrderedQueryable<MaintenanceRunEntity>? orderedQuery = null;
        foreach (var term in AdminCatalogProfiles.Maintenance.ParseSort(query.Sort))
        {
            orderedQuery = ApplyMaintenanceSort(orderedQuery ?? runsQuery, term);
        }

        orderedQuery ??= runsQuery.OrderByDescending(run => run.RequestedAtUtc);

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

    private static IOrderedQueryable<MaintenanceRunEntity> ApplyMaintenanceSort(
        IQueryable<MaintenanceRunEntity> source,
        GridSortTerm term)
        => (term.Field.ToLowerInvariant(), term.Direction) switch
        {
            ("operation", GridSortDirection.Asc) => source.OrderBy(run => run.Operation).ThenByDescending(run => run.RequestedAtUtc),
            ("operation", GridSortDirection.Desc) => source.OrderByDescending(run => run.Operation).ThenByDescending(run => run.RequestedAtUtc),
            ("status", GridSortDirection.Asc) => source.OrderBy(run => run.Status).ThenByDescending(run => run.RequestedAtUtc),
            ("status", GridSortDirection.Desc) => source.OrderByDescending(run => run.Status).ThenByDescending(run => run.RequestedAtUtc),
            ("requested", GridSortDirection.Asc) => source.OrderBy(run => run.RequestedAtUtc),
            _ => source.OrderByDescending(run => run.RequestedAtUtc)
        };
}

public sealed class EfTorrentAdminReader(TrackerConfigurationDbContext dbContext) : ITorrentAdminReader
{
    public async Task<PageResult<TorrentAdminDto>> ListAsync(GridQuery query, TorrentCatalogFilter filter, bool? isEnabled, bool? isPrivate, CancellationToken cancellationToken)
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

        torrentsQuery = filter switch
        {
            TorrentCatalogFilter.Enabled => torrentsQuery.Where(torrent => torrent.IsEnabled),
            TorrentCatalogFilter.Disabled => torrentsQuery.Where(torrent => !torrent.IsEnabled),
            TorrentCatalogFilter.Private => torrentsQuery.Where(torrent => torrent.IsPrivate),
            TorrentCatalogFilter.Public => torrentsQuery.Where(torrent => !torrent.IsPrivate),
            _ => torrentsQuery
        };

        IOrderedQueryable<TorrentConfigurationEntity>? orderedQuery = null;
        foreach (var term in AdminCatalogProfiles.Torrents.ParseSort(query.Sort))
        {
            orderedQuery = ApplyTorrentSort(orderedQuery ?? torrentsQuery, term);
        }

        orderedQuery ??= torrentsQuery.OrderBy(torrent => torrent.InfoHash);

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

    private static IOrderedQueryable<TorrentConfigurationEntity> ApplyTorrentSort(
        IQueryable<TorrentConfigurationEntity> source,
        GridSortTerm term)
        => (term.Field.ToLowerInvariant(), term.Direction) switch
        {
            ("enabled", GridSortDirection.Asc) => source.OrderBy(torrent => torrent.IsEnabled).ThenBy(torrent => torrent.InfoHash),
            ("enabled", GridSortDirection.Desc) => source.OrderByDescending(torrent => torrent.IsEnabled).ThenBy(torrent => torrent.InfoHash),
            ("private", GridSortDirection.Asc) => source.OrderBy(torrent => torrent.IsPrivate).ThenBy(torrent => torrent.InfoHash),
            ("private", GridSortDirection.Desc) => source.OrderByDescending(torrent => torrent.IsPrivate).ThenBy(torrent => torrent.InfoHash),
            ("interval", GridSortDirection.Asc) => source.OrderBy(torrent => torrent.Policy != null ? torrent.Policy.AnnounceIntervalSeconds : int.MaxValue).ThenBy(torrent => torrent.InfoHash),
            ("interval", GridSortDirection.Desc) => source.OrderByDescending(torrent => torrent.Policy != null ? torrent.Policy.AnnounceIntervalSeconds : int.MinValue).ThenBy(torrent => torrent.InfoHash),
            ("infohash", GridSortDirection.Desc) => source.OrderByDescending(torrent => torrent.InfoHash),
            _ => source.OrderBy(torrent => torrent.InfoHash)
        };
}

public sealed class EfPasskeyAdminReader(TrackerConfigurationDbContext dbContext) : IPasskeyAdminReader
{
    public async Task<PageResult<PasskeyAdminDto>> ListAsync(GridQuery query, PasskeyCatalogFilter filter, Guid? userId, bool? isRevoked, CancellationToken cancellationToken)
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

        passkeysQuery = filter switch
        {
            PasskeyCatalogFilter.Revoked => passkeysQuery.Where(passkey => passkey.IsRevoked),
            PasskeyCatalogFilter.Active => passkeysQuery.Where(passkey => !passkey.IsRevoked),
            PasskeyCatalogFilter.Expired => passkeysQuery.Where(passkey => passkey.ExpiresAtUtc.HasValue && passkey.ExpiresAtUtc < DateTime.UtcNow),
            _ => passkeysQuery
        };

        IOrderedQueryable<PasskeyCredentialEntity>? orderedQuery = null;
        foreach (var term in AdminCatalogProfiles.Passkeys.ParseSort(query.Sort))
        {
            orderedQuery = ApplyPasskeySort(orderedQuery ?? passkeysQuery, term);
        }

        orderedQuery ??= passkeysQuery.OrderBy(passkey => passkey.UserId).ThenBy(passkey => passkey.Passkey);

        var (passkeys, totalCount) = await orderedQuery.ToPageAsync(query.Page, query.PageSize, cancellationToken);

        return new PageResult<PasskeyAdminDto>(passkeys.Select(static passkey => new PasskeyAdminDto(
                passkey.Id,
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

    public async Task<PasskeyAdminDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var passkey = await dbContext.Passkeys
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        return passkey is null
            ? null
            : new PasskeyAdminDto(
                passkey.Id,
                MaskPasskey(passkey.Passkey),
                passkey.UserId,
                passkey.IsRevoked,
                passkey.ExpiresAtUtc.HasValue
                    ? new DateTimeOffset(DateTime.SpecifyKind(passkey.ExpiresAtUtc.Value, DateTimeKind.Utc))
                    : null,
                passkey.RowVersion);
    }

    private static string MaskPasskey(string passkey)
        => passkey.Length <= 6 ? $"pk:{passkey[0]}...{passkey[^1]}" : $"pk:{passkey[..4]}...{passkey[^2..]}";

    private static IOrderedQueryable<PasskeyCredentialEntity> ApplyPasskeySort(
        IQueryable<PasskeyCredentialEntity> source,
        GridSortTerm term)
        => (term.Field.ToLowerInvariant(), term.Direction) switch
        {
            ("expires", GridSortDirection.Asc) => source.OrderBy(passkey => passkey.ExpiresAtUtc ?? DateTime.MaxValue).ThenBy(passkey => passkey.UserId).ThenBy(passkey => passkey.Passkey),
            ("expires", GridSortDirection.Desc) => source.OrderByDescending(passkey => passkey.ExpiresAtUtc ?? DateTime.MaxValue).ThenBy(passkey => passkey.UserId).ThenBy(passkey => passkey.Passkey),
            ("version", GridSortDirection.Asc) => source.OrderBy(passkey => passkey.RowVersion).ThenBy(passkey => passkey.UserId).ThenBy(passkey => passkey.Passkey),
            ("version", GridSortDirection.Desc) => source.OrderByDescending(passkey => passkey.RowVersion).ThenBy(passkey => passkey.UserId).ThenBy(passkey => passkey.Passkey),
            ("userid", GridSortDirection.Desc) => source.OrderByDescending(passkey => passkey.UserId).ThenBy(passkey => passkey.Passkey),
            _ => source.OrderBy(passkey => passkey.UserId).ThenBy(passkey => passkey.Passkey)
        };
}

public sealed class EfTrackerAccessRightsAdminReader(TrackerConfigurationDbContext dbContext) : ITrackerAccessRightsAdminReader
{
    public async Task<PageResult<TrackerAccessAdminDto>> ListAsync(GridQuery query, TrackerAccessRightsFilter filter, bool? canUsePrivateTracker, CancellationToken cancellationToken)
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

        permissionsQuery = filter switch
        {
            TrackerAccessRightsFilter.Private => permissionsQuery.Where(permission => permission.CanUsePrivateTracker),
            TrackerAccessRightsFilter.Public => permissionsQuery.Where(permission => !permission.CanUsePrivateTracker),
            TrackerAccessRightsFilter.Seed => permissionsQuery.Where(permission => permission.CanSeed),
            TrackerAccessRightsFilter.Leech => permissionsQuery.Where(permission => permission.CanLeech),
            TrackerAccessRightsFilter.Scrape => permissionsQuery.Where(permission => permission.CanScrape),
            _ => permissionsQuery
        };

        IOrderedQueryable<UserPermissionEntity>? orderedQuery = null;
        foreach (var term in AdminCatalogProfiles.TrackerAccess.ParseSort(query.Sort))
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

    public async Task<TrackerAccessAdminDto?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var permission = await dbContext.Permissions
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);

        return permission is null
            ? null
            : new TrackerAccessAdminDto(
                permission.UserId,
                permission.CanLeech,
                permission.CanSeed,
                permission.CanScrape,
                permission.CanUsePrivateTracker,
                permission.RowVersion);
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
    public async Task<PageResult<BanRuleAdminDto>> ListAsync(GridQuery query, BanCatalogFilter filter, string? scope, CancellationToken cancellationToken)
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

        bansQuery = filter switch
        {
            BanCatalogFilter.Active => bansQuery.Where(ban => !ban.ExpiresAtUtc.HasValue || ban.ExpiresAtUtc > DateTime.UtcNow),
            BanCatalogFilter.Expired => bansQuery.Where(ban => ban.ExpiresAtUtc.HasValue && ban.ExpiresAtUtc <= DateTime.UtcNow),
            _ => bansQuery
        };

        IOrderedQueryable<BanRuleEntity>? orderedQuery = null;
        foreach (var term in AdminCatalogProfiles.Bans.ParseSort(query.Sort))
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

    public async Task<BanRuleAdminDto?> GetAsync(string scope, string subject, CancellationToken cancellationToken)
    {
        var ban = await dbContext.Bans
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Scope == scope && item.Subject == subject, cancellationToken);

        return ban is null
            ? null
            : new BanRuleAdminDto(
                ban.Scope,
                ban.Subject,
                ban.Reason,
                ban.ExpiresAtUtc.HasValue
                    ? new DateTimeOffset(DateTime.SpecifyKind(ban.ExpiresAtUtc.Value, DateTimeKind.Utc))
                    : null,
                ban.RowVersion);
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

public sealed class TrackerNodeConfigAdminReader(
    ITrackerNodeConfigurationReader trackerNodeConfigurationReader) : ITrackerNodeConfigAdminReader
{
    public async Task<PageResult<TrackerNodeCatalogItemDto>> ListAsync(GridQuery query, CancellationToken cancellationToken)
    {
        query = query.Normalize(defaultPageSize: 25, maxPageSize: 100);
        var normalizedSearch = query.NormalizedSearch;
        var configurations = await trackerNodeConfigurationReader.ListTrackerNodeConfigurationsAsync(cancellationToken);

        var items = new List<TrackerNodeCatalogItemDto>(configurations.Count);
        foreach (var configuration in configurations)
        {
            var validation = await trackerNodeConfigurationReader.ValidateTrackerNodeConfigurationAsync(configuration.Configuration, cancellationToken);
            items.Add(ToCatalogItem(configuration, validation));
        }

        IEnumerable<TrackerNodeCatalogItemDto> filtered = items;
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            filtered = filtered.Where(item =>
                item.NodeKey.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                item.NodeName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                item.NodeId.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                item.Environment.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                item.Region.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                item.UpdatedBy.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase));
        }

        IOrderedEnumerable<TrackerNodeCatalogItemDto>? ordered = null;
        foreach (var term in AdminCatalogProfiles.TrackerNodes.ParseSort(query.Sort))
        {
            ordered = ApplyTrackerNodeSort(ordered ?? filtered, term);
        }

        ordered ??= filtered.OrderBy(item => item.NodeKey, StringComparer.OrdinalIgnoreCase);
        var totalCount = ordered.Count();
        var pageItems = ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArray()
            .AsReadOnly();

        return new PageResult<TrackerNodeCatalogItemDto>(pageItems, totalCount, query.Page, query.PageSize);
    }

    public async Task<TrackerNodeConfigViewDto?> GetAsync(string nodeKey, CancellationToken cancellationToken)
    {
        var snapshot = await trackerNodeConfigurationReader.GetEffectiveTrackerNodeConfigurationAsync(nodeKey, cancellationToken);
        if (snapshot is null)
        {
            return null;
        }

        var validation = await trackerNodeConfigurationReader.ValidateTrackerNodeConfigurationAsync(snapshot.Configuration, cancellationToken);
        var config = snapshot.Configuration;

        return new TrackerNodeConfigViewDto(
            new TrackerNodeOverviewDto(
                snapshot.NodeKey,
                snapshot.Version,
                config.Identity.NodeId,
                config.Identity.NodeName,
                config.Identity.Environment,
                config.Identity.Region,
                config.Identity.PublicBaseUrl,
                config.Identity.InternalBaseUrl,
                config.Http.EnableAnnounce,
                config.Http.EnableScrape,
                config.Udp.Enabled,
                config.Policy.EnablePublicTracker,
                config.Policy.EnablePrivateTracker,
                config.Runtime.EnableIPv6Peers,
                config.Runtime.ShardCount,
                config.Http.CompactResponsesByDefault,
                config.Http.AllowNonCompactResponses,
                snapshot.RequiresRestart,
                snapshot.ApplyMode.ToString(),
                snapshot.UpdatedAtUtc,
                snapshot.UpdatedBy),
            new TrackerNodeCapabilitiesDto(
                config.Identity.SupportsHttp,
                config.Identity.SupportsUdp,
                config.Identity.SupportsPublicTracker,
                config.Identity.SupportsPrivateTracker,
                config.Http.AllowPasskeyInPath || config.Http.AllowPasskeyInQuery,
                config.Http.AllowClientIpOverride,
                config.Http.AllowNonCompactResponses,
                config.Runtime.EnableIPv6Peers,
                config.Postgres.PersistAudit,
                config.Postgres.PersistTelemetry,
                config.Observability.EnableDiagnosticsEndpoints),
            new TrackerNodeProtocolViewDto(
                config.Http.AnnounceRoute,
                config.Http.PrivateAnnounceRoute,
                config.Http.ScrapeRoute,
                config.Http.EnableAnnounce,
                config.Http.EnableScrape,
                config.Udp.Enabled,
                config.Udp.BindAddress,
                config.Udp.Port,
                config.Udp.EnableScrape,
                config.Http.DefaultAnnounceIntervalSeconds,
                config.Http.MinAnnounceIntervalSeconds,
                config.Http.DefaultNumWant,
                config.Http.MaxNumWant,
                config.Http.CompactResponsesByDefault,
                config.Http.AllowNonCompactResponses,
                config.Http.AllowPasskeyInPath,
                config.Http.AllowPasskeyInQuery,
                config.Http.AllowClientIpOverride),
            new TrackerNodeRuntimeViewDto(
                config.Runtime.ShardCount,
                config.Runtime.PeerTtlSeconds,
                config.Runtime.CleanupIntervalSeconds,
                config.Runtime.MaxPeersPerResponse,
                config.Runtime.MaxPeersPerSwarm,
                config.Runtime.PreferLocalShardPeers,
                config.Runtime.EnableCompletedAccounting,
                config.Runtime.EnableIPv6Peers,
                AnnounceDisabled: !config.Http.EnableAnnounce,
                ScrapeDisabled: !config.Http.EnableScrape,
                UdpDisabled: !config.Udp.Enabled,
                GlobalMaintenanceMode: false,
                ReadOnlyMode: false),
            new TrackerNodeCoordinationViewDto(
                BuildRedisSummary(config.Redis),
                BuildPostgresSummary(config.Postgres),
                config.Redis.KeyPrefix,
                config.Redis.InvalidationChannel,
                config.Postgres.MigrateOnStart,
                config.Postgres.PersistTelemetry,
                config.Postgres.PersistAudit,
                config.Postgres.TelemetryBatchSize,
                config.Postgres.TelemetryFlushIntervalMilliseconds,
                config.Redis.HeartbeatTtlSeconds,
                config.Redis.OwnershipLeaseDurationSeconds,
                config.Redis.OwnershipRefreshIntervalSeconds,
                config.Redis.SwarmSummaryPublishIntervalSeconds,
                config.Redis.SwarmSummaryTtlSeconds),
            new TrackerNodePolicyViewDto(
                config.Policy.EnablePublicTracker,
                config.Policy.EnablePrivateTracker,
                config.Policy.RequirePasskeyForPrivateTracker,
                config.Policy.AllowPublicScrape,
                config.Policy.AllowPrivateScrape,
                config.Policy.DefaultTorrentVisibility,
                config.Policy.StrictnessProfile,
                config.Policy.CompatibilityMode,
                config.AbuseProtection.HardMaxNumWant,
                EmergencyAbuseMitigation: false,
                PolicyFreezeMode: false,
                IPv6Frozen: false),
            new TrackerNodeObservabilityViewDto(
                config.Observability.EnableHealthEndpoints,
                config.Observability.EnableMetrics,
                config.Observability.EnableTracing,
                config.Observability.EnableDiagnosticsEndpoints,
                config.Observability.LiveRoute,
                config.Observability.ReadyRoute,
                config.Observability.StartupRoute,
                RequireRedisForReadiness: true,
                RequirePostgresForReadiness: true),
            new TrackerNodeAbuseViewDto(
                config.AbuseProtection.MaxAnnounceQueryLength,
                config.AbuseProtection.MaxScrapeQueryLength,
                config.AbuseProtection.MaxQueryParameterCount,
                config.AbuseProtection.HardMaxNumWant,
                config.AbuseProtection.EnableAnnouncePasskeyRateLimit,
                config.AbuseProtection.AnnouncePerPasskeyPerSecond,
                config.AbuseProtection.EnableAnnounceIpRateLimit,
                config.AbuseProtection.AnnouncePerIpPerSecond,
                config.AbuseProtection.EnableScrapeIpRateLimit,
                config.AbuseProtection.ScrapePerIpPerSecond,
                config.AbuseProtection.RejectOversizedRequests,
                config.AbuseProtection.MaxScrapeInfoHashes),
            new ConfigValidationDto(
                validation.IsValid,
                validation.Issues.Where(static issue => issue.Severity == TrackerConfigValidationSeverity.Error).Select(static issue => issue.Message).ToArray(),
                validation.Issues.Where(static issue => issue.Severity == TrackerConfigValidationSeverity.Warning).Select(static issue => issue.Message).ToArray()));
    }

    private static TrackerNodeCatalogItemDto ToCatalogItem(
        TrackerNodeConfigurationDto configuration,
        TrackerNodeConfigurationValidationResultDto validation)
    {
        var config = configuration.Configuration;
        return new TrackerNodeCatalogItemDto(
            configuration.NodeKey,
            config.Identity.NodeName,
            config.Identity.NodeId,
            config.Identity.Environment,
            config.Identity.Region,
            config.Http.EnableAnnounce,
            config.Udp.Enabled,
            config.Policy.EnablePublicTracker,
            config.Policy.EnablePrivateTracker,
            configuration.RequiresRestart,
            configuration.ApplyMode.ToString(),
            configuration.UpdatedAtUtc,
            configuration.UpdatedBy,
            validation.Issues.Count(static issue => issue.Severity == TrackerConfigValidationSeverity.Error),
            validation.Issues.Count(static issue => issue.Severity == TrackerConfigValidationSeverity.Warning));
    }

    private static IOrderedEnumerable<TrackerNodeCatalogItemDto> ApplyTrackerNodeSort(
        IEnumerable<TrackerNodeCatalogItemDto> source,
        GridSortTerm term)
        => (term.Field, term.Direction) switch
        {
            ("nodename", GridSortDirection.Asc) => source.OrderBy(item => item.NodeName, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.NodeKey, StringComparer.OrdinalIgnoreCase),
            ("nodename", GridSortDirection.Desc) => source.OrderByDescending(item => item.NodeName, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.NodeKey, StringComparer.OrdinalIgnoreCase),
            ("environment", GridSortDirection.Asc) => source.OrderBy(item => item.Environment, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.NodeKey, StringComparer.OrdinalIgnoreCase),
            ("environment", GridSortDirection.Desc) => source.OrderByDescending(item => item.Environment, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.NodeKey, StringComparer.OrdinalIgnoreCase),
            ("region", GridSortDirection.Asc) => source.OrderBy(item => item.Region, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.NodeKey, StringComparer.OrdinalIgnoreCase),
            ("region", GridSortDirection.Desc) => source.OrderByDescending(item => item.Region, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.NodeKey, StringComparer.OrdinalIgnoreCase),
            ("updated", GridSortDirection.Asc) => source.OrderBy(item => item.UpdatedAtUtc).ThenBy(item => item.NodeKey, StringComparer.OrdinalIgnoreCase),
            ("updated", GridSortDirection.Desc) => source.OrderByDescending(item => item.UpdatedAtUtc).ThenBy(item => item.NodeKey, StringComparer.OrdinalIgnoreCase),
            ("nodekey", GridSortDirection.Desc) => source.OrderByDescending(item => item.NodeKey, StringComparer.OrdinalIgnoreCase),
            _ => source.OrderBy(item => item.NodeKey, StringComparer.OrdinalIgnoreCase)
        };

    private static TrackerNodeDependencySummaryDto BuildRedisSummary(RedisCoordinationConfig config)
    {
        if (!config.Enabled)
        {
            return new TrackerNodeDependencySummaryDto(false, "Disabled", Healthy: false);
        }

        try
        {
            var options = ConfigurationOptions.Parse(config.Configuration);
            return new TrackerNodeDependencySummaryDto(
                true,
                $"Configured ({options.EndPoints.Count} endpoint{(options.EndPoints.Count == 1 ? string.Empty : "s")}, prefix {config.KeyPrefix})",
                Healthy: true);
        }
        catch
        {
            return new TrackerNodeDependencySummaryDto(true, $"Configured (prefix {config.KeyPrefix})", Healthy: true);
        }
    }

    private static TrackerNodeDependencySummaryDto BuildPostgresSummary(PostgresPersistenceConfig config)
    {
        if (!config.Enabled)
        {
            return new TrackerNodeDependencySummaryDto(false, "Disabled", Healthy: false);
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(config.ConnectionString);
            return new TrackerNodeDependencySummaryDto(true, $"Configured ({builder.Host}/{builder.Database})", Healthy: true);
        }
        catch
        {
            return new TrackerNodeDependencySummaryDto(true, "Configured", Healthy: true);
        }
    }
}

#pragma warning disable CS0618
public sealed class ConfigurationMutationOrchestrator(
    IConfigurationMutationService configurationMutationService,
    IConfigurationMutationPreviewService configurationMutationPreviewService,
    IConfigurationMaintenanceService configurationMaintenanceService) : IAdminMutationOrchestrator
{
    public Task<TrackerNodeConfigurationDto> UpsertTrackerNodeConfigurationAsync(string nodeKey, TrackerNodeConfigurationUpsertRequest request, AdminMutationContext context, CancellationToken cancellationToken)
        => configurationMutationService.UpsertTrackerNodeConfigurationAsync(nodeKey, request, context, cancellationToken);

    public Task DeleteTrackerNodeConfigurationAsync(string nodeKey, long? expectedVersion, AdminMutationContext context, CancellationToken cancellationToken)
        => configurationMutationService.DeleteTrackerNodeConfigurationAsync(nodeKey, expectedVersion, context, cancellationToken);

    private static string MaskPasskey(string passkey)
        => passkey.Length <= 6 ? $"pk:{passkey[0]}...{passkey[^1]}" : $"pk:{passkey[..4]}...{passkey[^2..]}";

    private static BulkPasskeyOperationItemDto CreatePasskeySuccessItem(PasskeyAccessDto snapshot, string? newPasskey = null, string? newPasskeyMask = null)
        => new(
            MaskPasskey(snapshot.Passkey),
            true,
            null,
            null,
            new PasskeyAdminDto(
                snapshot.Id,
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

    public Task DeleteTorrentAsync(string infoHash, long? expectedVersion, AdminMutationContext context, CancellationToken cancellationToken)
        => configurationMutationService.DeleteTorrentAsync(infoHash, expectedVersion, context, cancellationToken);

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

    public Task<PasskeyAccessDto> CreatePasskeyAsync(PasskeyCreateRequest request, AdminMutationContext context, CancellationToken cancellationToken)
        => configurationMutationService.CreatePasskeyAsync(request, context, cancellationToken);

    public Task<PasskeyAccessDto> UpsertPasskeyByIdAsync(Guid id, PasskeyUpsertRequest request, AdminMutationContext context, CancellationToken cancellationToken)
        => configurationMutationService.UpsertPasskeyByIdAsync(id, request, context, cancellationToken);

    public Task<PasskeyAccessDto> RevokePasskeyByIdAsync(Guid id, PasskeyRevokeRequest request, AdminMutationContext context, CancellationToken cancellationToken)
        => configurationMutationService.RevokePasskeyByIdAsync(id, request, context, cancellationToken);

    public Task<(PasskeyAccessDto RevokedSnapshot, PasskeyAccessDto NewSnapshot)> RotatePasskeyByIdAsync(Guid id, PasskeyRotateRequest request, AdminMutationContext context, CancellationToken cancellationToken)
        => configurationMutationService.RotatePasskeyByIdAsync(id, request, context, cancellationToken);

    public Task DeletePasskeyByIdAsync(Guid id, long? expectedVersion, AdminMutationContext context, CancellationToken cancellationToken)
        => configurationMutationService.DeletePasskeyByIdAsync(id, expectedVersion, context, cancellationToken);

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

    public Task DeleteTrackerAccessRightsAsync(Guid userId, long? expectedVersion, AdminMutationContext context, CancellationToken cancellationToken)
        => configurationMutationService.DeleteTrackerAccessRightsAsync(userId, expectedVersion, context, cancellationToken);

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

    public Task<BanRuleDto> ExpireBanRuleAsync(string scope, string subject, BanRuleExpireRequest request, AdminMutationContext context, CancellationToken cancellationToken)
        => configurationMutationService.ExpireBanRuleAsync(scope, subject, request, context, cancellationToken);

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
        services.AddScoped<ITrackerNodeConfigAdminReader, TrackerNodeConfigAdminReader>();
        services.AddScoped<IAdminMutationOrchestrator, ConfigurationMutationOrchestrator>();
        return services;
    }
}
