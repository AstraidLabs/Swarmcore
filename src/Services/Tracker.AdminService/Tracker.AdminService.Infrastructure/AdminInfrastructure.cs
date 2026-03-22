using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Swarmcore.Caching.Redis;
using Swarmcore.Contracts.Admin;
using Swarmcore.Contracts.Configuration;
using Swarmcore.Contracts.Runtime;
using Tracker.AdminService.Application;
using Tracker.ConfigurationService.Application;
using Tracker.ConfigurationService.Infrastructure;

namespace Tracker.AdminService.Infrastructure;

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
    public async Task<IReadOnlyCollection<AuditRecordDto>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var (normalizedPage, normalizedPageSize) = NormalizePaging(page, pageSize);
        var offset = (normalizedPage - 1) * normalizedPageSize;
        var records = await dbContext.AuditRecords
            .AsNoTracking()
            .OrderByDescending(static record => record.OccurredAtUtc)
            .Skip(offset)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        return records
            .Select(static record => new AuditRecordDto(
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
            .ToArray();
    }

    private static (int Page, int PageSize) NormalizePaging(int page, int pageSize)
        => (page < 1 ? 1 : page, Math.Clamp(pageSize, 1, 200));
}

public sealed class EfMaintenanceRunReader(TrackerConfigurationDbContext dbContext) : IMaintenanceRunReader
{
    public async Task<IReadOnlyCollection<MaintenanceRunDto>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var (normalizedPage, normalizedPageSize) = NormalizePaging(page, pageSize);
        var offset = (normalizedPage - 1) * normalizedPageSize;
        var runs = await dbContext.MaintenanceRuns
            .AsNoTracking()
            .OrderByDescending(static run => run.RequestedAtUtc)
            .Skip(offset)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        return runs
            .Select(static run => new MaintenanceRunDto(
                run.Id,
                run.Operation,
                run.RequestedBy,
                new DateTimeOffset(DateTime.SpecifyKind(run.RequestedAtUtc, DateTimeKind.Utc)),
                run.Status,
                run.CorrelationId))
            .ToArray();
    }

    private static (int Page, int PageSize) NormalizePaging(int page, int pageSize)
        => (page < 1 ? 1 : page, Math.Clamp(pageSize, 1, 200));
}

public sealed class EfTorrentAdminReader(TrackerConfigurationDbContext dbContext) : ITorrentAdminReader
{
    public async Task<IReadOnlyCollection<TorrentAdminDto>> ListAsync(string? search, bool? isEnabled, bool? isPrivate, int page, int pageSize, CancellationToken cancellationToken)
    {
        var (normalizedPage, normalizedPageSize) = NormalizePaging(page, pageSize);
        var offset = (normalizedPage - 1) * normalizedPageSize;
        var query = dbContext.Torrents
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(torrent => EF.Functions.ILike(torrent.InfoHash, pattern));
        }

        if (isEnabled.HasValue)
        {
            query = query.Where(torrent => torrent.IsEnabled == isEnabled.Value);
        }

        if (isPrivate.HasValue)
        {
            query = query.Where(torrent => torrent.IsPrivate == isPrivate.Value);
        }

        var torrents = await query
            .Include(static torrent => torrent.Policy)
            .OrderBy(static torrent => torrent.InfoHash)
            .Skip(offset)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        return torrents
            .Select(static torrent => MapTorrent(torrent))
            .ToArray();
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

    private static (int Page, int PageSize) NormalizePaging(int page, int pageSize)
        => (page < 1 ? 1 : page, Math.Clamp(pageSize, 1, 200));
}

public sealed class EfPasskeyAdminReader(TrackerConfigurationDbContext dbContext) : IPasskeyAdminReader
{
    public async Task<IReadOnlyCollection<PasskeyAdminDto>> ListAsync(Guid? userId, bool? isRevoked, int page, int pageSize, CancellationToken cancellationToken)
    {
        var (normalizedPage, normalizedPageSize) = NormalizePaging(page, pageSize);
        var offset = (normalizedPage - 1) * normalizedPageSize;
        var query = dbContext.Passkeys
            .AsNoTracking()
            .AsQueryable();

        if (userId.HasValue)
        {
            query = query.Where(passkey => passkey.UserId == userId.Value);
        }

        if (isRevoked.HasValue)
        {
            query = query.Where(passkey => passkey.IsRevoked == isRevoked.Value);
        }

        var passkeys = await query
            .OrderBy(static passkey => passkey.UserId)
            .ThenBy(static passkey => passkey.Passkey)
            .Skip(offset)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        return passkeys
            .Select(static passkey => new PasskeyAdminDto(
                MaskPasskey(passkey.Passkey),
                passkey.UserId,
                passkey.IsRevoked,
                passkey.ExpiresAtUtc.HasValue
                    ? new DateTimeOffset(DateTime.SpecifyKind(passkey.ExpiresAtUtc.Value, DateTimeKind.Utc))
                    : null,
                passkey.RowVersion))
            .ToArray();
    }

    private static string MaskPasskey(string passkey)
        => passkey.Length <= 6 ? $"pk:{passkey[0]}...{passkey[^1]}" : $"pk:{passkey[..4]}...{passkey[^2..]}";

    private static (int Page, int PageSize) NormalizePaging(int page, int pageSize)
        => (page < 1 ? 1 : page, Math.Clamp(pageSize, 1, 200));
}

public sealed class EfUserPermissionAdminReader(TrackerConfigurationDbContext dbContext) : IUserPermissionAdminReader
{
    public async Task<IReadOnlyCollection<UserPermissionAdminDto>> ListAsync(bool? canUsePrivateTracker, int page, int pageSize, CancellationToken cancellationToken)
    {
        var (normalizedPage, normalizedPageSize) = NormalizePaging(page, pageSize);
        var offset = (normalizedPage - 1) * normalizedPageSize;
        var query = dbContext.Permissions
            .AsNoTracking()
            .AsQueryable();

        if (canUsePrivateTracker.HasValue)
        {
            query = query.Where(permission => permission.CanUsePrivateTracker == canUsePrivateTracker.Value);
        }

        var permissions = await query
            .OrderBy(static permission => permission.UserId)
            .Skip(offset)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        return permissions
            .Select(static permission => new UserPermissionAdminDto(
                permission.UserId,
                permission.CanLeech,
                permission.CanSeed,
                permission.CanScrape,
                permission.CanUsePrivateTracker,
                permission.RowVersion))
            .ToArray();
    }

    private static (int Page, int PageSize) NormalizePaging(int page, int pageSize)
        => (page < 1 ? 1 : page, Math.Clamp(pageSize, 1, 200));
}

public sealed class EfBanAdminReader(TrackerConfigurationDbContext dbContext) : IBanAdminReader
{
    public async Task<IReadOnlyCollection<BanRuleAdminDto>> ListAsync(string? scope, int page, int pageSize, CancellationToken cancellationToken)
    {
        var (normalizedPage, normalizedPageSize) = NormalizePaging(page, pageSize);
        var offset = (normalizedPage - 1) * normalizedPageSize;
        var query = dbContext.Bans
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(scope))
        {
            query = query.Where(ban => ban.Scope == scope);
        }

        var bans = await query
            .OrderBy(static ban => ban.Scope)
            .ThenBy(static ban => ban.Subject)
            .Skip(offset)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        return bans
            .Select(static ban => new BanRuleAdminDto(
                ban.Scope,
                ban.Subject,
                ban.Reason,
                ban.ExpiresAtUtc.HasValue
                    ? new DateTimeOffset(DateTime.SpecifyKind(ban.ExpiresAtUtc.Value, DateTimeKind.Utc))
                    : null,
                ban.RowVersion))
            .ToArray();
    }

    private static (int Page, int PageSize) NormalizePaging(int page, int pageSize)
        => (page < 1 ? 1 : page, Math.Clamp(pageSize, 1, 200));
}

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

    public Task<UserPermissionSnapshotDto> UpsertUserPermissionsAsync(Guid userId, UserPermissionUpsertRequest request, AdminMutationContext context, CancellationToken cancellationToken)
        => configurationMutationService.UpsertUserPermissionsAsync(userId, request, context, cancellationToken);

    public async Task<BulkOperationResultDto> BulkUpsertUserPermissionsAsync(IReadOnlyCollection<BulkUserPermissionUpsertItem> items, AdminMutationContext context, CancellationToken cancellationToken)
    {
        var results = new List<BulkUserPermissionOperationItemDto>(items.Count);

        foreach (var item in items)
        {
            try
            {
                var snapshot = await configurationMutationService.UpsertUserPermissionsAsync(
                    item.UserId,
                    new UserPermissionUpsertRequest(item.CanLeech, item.CanSeed, item.CanScrape, item.CanUsePrivateTracker, item.ExpectedVersion),
                    context,
                    cancellationToken);

                results.Add(new BulkUserPermissionOperationItemDto(
                    item.UserId,
                    true,
                    null,
                    null,
                    new UserPermissionAdminDto(
                        snapshot.UserId,
                        snapshot.CanLeech,
                        snapshot.CanSeed,
                        snapshot.CanScrape,
                        snapshot.CanUsePrivateTracker,
                        snapshot.Version)));
            }
            catch (ConfigurationConcurrencyException exception)
            {
                results.Add(new BulkUserPermissionOperationItemDto(item.UserId, false, "concurrency_conflict", exception.Message, null));
            }
        }

        return new BulkOperationResultDto(
            results.Count,
            results.Count(static item => item.Succeeded),
            results.Count(static item => !item.Succeeded),
            Array.Empty<BulkPasskeyOperationItemDto>(),
            Array.Empty<BulkTorrentOperationItemDto>(),
            results,
            Array.Empty<BulkBanOperationItemDto>());
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

public static class AdminInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddAdminInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddConfigurationInfrastructure(configuration);
        services.AddScoped<IClusterOverviewReader, RedisClusterOverviewReader>();
        services.AddScoped<IAuditRecordReader, EfAuditRecordReader>();
        services.AddScoped<IMaintenanceRunReader, EfMaintenanceRunReader>();
        services.AddScoped<ITorrentAdminReader, EfTorrentAdminReader>();
        services.AddScoped<IPasskeyAdminReader, EfPasskeyAdminReader>();
        services.AddScoped<IUserPermissionAdminReader, EfUserPermissionAdminReader>();
        services.AddScoped<IBanAdminReader, EfBanAdminReader>();
        services.AddScoped<IAdminMutationOrchestrator, ConfigurationMutationOrchestrator>();
        return services;
    }
}
