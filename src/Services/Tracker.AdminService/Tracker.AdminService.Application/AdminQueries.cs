using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Swarmcore.Contracts.Admin;
using Swarmcore.Contracts.Configuration;

namespace Tracker.AdminService.Application;

public sealed record GetClusterOverviewQuery() : IRequest<ClusterOverviewDto>;

public sealed record ListAuditRecordsQuery(int Page = 1, int PageSize = 50) : IRequest<IReadOnlyCollection<AuditRecordDto>>;

public sealed record ListMaintenanceRunsQuery(int Page = 1, int PageSize = 50) : IRequest<IReadOnlyCollection<MaintenanceRunDto>>;

public sealed record ListTorrentsQuery(string? Search = null, bool? IsEnabled = null, bool? IsPrivate = null, int Page = 1, int PageSize = 50)
    : IRequest<IReadOnlyCollection<TorrentAdminDto>>;

public sealed record GetTorrentDetailQuery(string InfoHash) : IRequest<TorrentAdminDto?>;

public sealed record ListPasskeysQuery(Guid? UserId = null, bool? IsRevoked = null, int Page = 1, int PageSize = 50)
    : IRequest<IReadOnlyCollection<PasskeyAdminDto>>;

public sealed record ListUserPermissionsQuery(bool? CanUsePrivateTracker = null, int Page = 1, int PageSize = 50)
    : IRequest<IReadOnlyCollection<UserPermissionAdminDto>>;

public sealed record ListBansQuery(string? Scope = null, int Page = 1, int PageSize = 50)
    : IRequest<IReadOnlyCollection<BanRuleAdminDto>>;

public sealed record UpsertTorrentPolicyAdminCommand(string InfoHash, TorrentPolicyUpsertRequest Request, AdminMutationContext Context)
    : IRequest<TorrentPolicyDto>;

public sealed record BulkActivateTorrentsAdminCommand(IReadOnlyCollection<BulkTorrentActivationItem> Items, AdminMutationContext Context)
    : IRequest<BulkOperationResultDto>;

public sealed record BulkDeactivateTorrentsAdminCommand(IReadOnlyCollection<BulkTorrentActivationItem> Items, AdminMutationContext Context)
    : IRequest<BulkOperationResultDto>;

public sealed record BulkUpsertTorrentPoliciesAdminCommand(IReadOnlyCollection<BulkTorrentPolicyUpsertItem> Items, AdminMutationContext Context)
    : IRequest<BulkOperationResultDto>;

public sealed record DryRunBulkUpsertTorrentPoliciesAdminCommand(IReadOnlyCollection<BulkTorrentPolicyUpsertItem> Items)
    : IRequest<BulkDryRunResultDto>;

public sealed record UpsertPasskeyAdminCommand(string Passkey, PasskeyUpsertRequest Request, AdminMutationContext Context)
    : IRequest<PasskeyAccessDto>;

public sealed record BulkRevokePasskeysAdminCommand(IReadOnlyCollection<BulkPasskeyRevokeItem> Items, AdminMutationContext Context)
    : IRequest<BulkOperationResultDto>;

public sealed record BulkRotatePasskeysAdminCommand(IReadOnlyCollection<BulkPasskeyRotateItem> Items, AdminMutationContext Context)
    : IRequest<BulkOperationResultDto>;

public sealed record UpsertUserPermissionsAdminCommand(Guid UserId, UserPermissionUpsertRequest Request, AdminMutationContext Context)
    : IRequest<UserPermissionSnapshotDto>;

public sealed record BulkUpsertUserPermissionsAdminCommand(IReadOnlyCollection<BulkUserPermissionUpsertItem> Items, AdminMutationContext Context)
    : IRequest<BulkOperationResultDto>;

public sealed record UpsertBanAdminCommand(string Scope, string Subject, BanRuleUpsertRequest Request, AdminMutationContext Context)
    : IRequest<BanRuleDto>;

public sealed record BulkUpsertBansAdminCommand(IReadOnlyCollection<BulkBanRuleUpsertItem> Items, AdminMutationContext Context)
    : IRequest<BulkOperationResultDto>;

public sealed record BulkExpireBansAdminCommand(IReadOnlyCollection<BulkBanRuleExpireItem> Items, AdminMutationContext Context)
    : IRequest<BulkOperationResultDto>;

public sealed record BulkDeleteBansAdminCommand(IReadOnlyCollection<BulkBanRuleDeleteItem> Items, AdminMutationContext Context)
    : IRequest<BulkOperationResultDto>;

public sealed record DeleteBanAdminCommand(string Scope, string Subject, long? ExpectedVersion, AdminMutationContext Context)
    : IRequest<Unit>;

public sealed record TriggerCacheRefreshAdminCommand(string Operation, AdminMutationContext Context)
    : IRequest<Unit>;

public interface IClusterOverviewReader
{
    Task<ClusterOverviewDto> GetAsync(CancellationToken cancellationToken);
}

// ─── Cluster Shard / Node State Admin Readers ─────────────────────────────────

/// <summary>
/// Reads full cluster shard ownership diagnostics from the coordination layer.
/// </summary>
public interface IClusterShardDiagnosticsReader
{
    Task<ClusterShardDiagnosticsDto> GetShardDiagnosticsAsync(int totalShardCount, CancellationToken cancellationToken);
}

/// <summary>
/// Reads node cluster state (heartbeat + operational state) for all known nodes.
/// </summary>
public interface IClusterNodeStateReader
{
    Task<IReadOnlyCollection<ClusterNodeStateDto>> GetAllNodeStatesAsync(CancellationToken cancellationToken);
    Task<ClusterNodeStateDto?> GetNodeStateAsync(string nodeId, CancellationToken cancellationToken);
}

public sealed record GetClusterShardDiagnosticsQuery(int TotalShardCount = 256) : IRequest<ClusterShardDiagnosticsDto>;
public sealed record GetClusterNodeStatesQuery() : IRequest<IReadOnlyCollection<ClusterNodeStateDto>>;

internal sealed class GetClusterShardDiagnosticsQueryHandler(IClusterShardDiagnosticsReader reader)
    : IRequestHandler<GetClusterShardDiagnosticsQuery, ClusterShardDiagnosticsDto>
{
    public Task<ClusterShardDiagnosticsDto> Handle(GetClusterShardDiagnosticsQuery request, CancellationToken cancellationToken)
        => reader.GetShardDiagnosticsAsync(request.TotalShardCount, cancellationToken);
}

internal sealed class GetClusterNodeStatesQueryHandler(IClusterNodeStateReader reader)
    : IRequestHandler<GetClusterNodeStatesQuery, IReadOnlyCollection<ClusterNodeStateDto>>
{
    public Task<IReadOnlyCollection<ClusterNodeStateDto>> Handle(GetClusterNodeStatesQuery request, CancellationToken cancellationToken)
        => reader.GetAllNodeStatesAsync(cancellationToken);
}

public interface IAuditRecordReader
{
    Task<IReadOnlyCollection<AuditRecordDto>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);
}

public interface IMaintenanceRunReader
{
    Task<IReadOnlyCollection<MaintenanceRunDto>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);
}

public interface ITorrentAdminReader
{
    Task<IReadOnlyCollection<TorrentAdminDto>> ListAsync(string? search, bool? isEnabled, bool? isPrivate, int page, int pageSize, CancellationToken cancellationToken);
    Task<TorrentAdminDto?> GetAsync(string infoHash, CancellationToken cancellationToken);
}

public interface IPasskeyAdminReader
{
    Task<IReadOnlyCollection<PasskeyAdminDto>> ListAsync(Guid? userId, bool? isRevoked, int page, int pageSize, CancellationToken cancellationToken);
}

public interface IUserPermissionAdminReader
{
    Task<IReadOnlyCollection<UserPermissionAdminDto>> ListAsync(bool? canUsePrivateTracker, int page, int pageSize, CancellationToken cancellationToken);
}

public interface IBanAdminReader
{
    Task<IReadOnlyCollection<BanRuleAdminDto>> ListAsync(string? scope, int page, int pageSize, CancellationToken cancellationToken);
}

public interface IAdminMutationOrchestrator
{
    Task<TorrentPolicyDto> UpsertTorrentPolicyAsync(string infoHash, TorrentPolicyUpsertRequest request, AdminMutationContext context, CancellationToken cancellationToken);
    Task<BulkOperationResultDto> BulkActivateTorrentsAsync(IReadOnlyCollection<BulkTorrentActivationItem> items, AdminMutationContext context, CancellationToken cancellationToken);
    Task<BulkOperationResultDto> BulkDeactivateTorrentsAsync(IReadOnlyCollection<BulkTorrentActivationItem> items, AdminMutationContext context, CancellationToken cancellationToken);
    Task<BulkOperationResultDto> BulkUpsertTorrentPoliciesAsync(IReadOnlyCollection<BulkTorrentPolicyUpsertItem> items, AdminMutationContext context, CancellationToken cancellationToken);
    Task<BulkDryRunResultDto> DryRunBulkUpsertTorrentPoliciesAsync(IReadOnlyCollection<BulkTorrentPolicyUpsertItem> items, CancellationToken cancellationToken);
    Task<PasskeyAccessDto> UpsertPasskeyAsync(string passkey, PasskeyUpsertRequest request, AdminMutationContext context, CancellationToken cancellationToken);
    Task<BulkOperationResultDto> BulkRevokePasskeysAsync(IReadOnlyCollection<BulkPasskeyRevokeItem> items, AdminMutationContext context, CancellationToken cancellationToken);
    Task<BulkOperationResultDto> BulkRotatePasskeysAsync(IReadOnlyCollection<BulkPasskeyRotateItem> items, AdminMutationContext context, CancellationToken cancellationToken);
    Task<UserPermissionSnapshotDto> UpsertUserPermissionsAsync(Guid userId, UserPermissionUpsertRequest request, AdminMutationContext context, CancellationToken cancellationToken);
    Task<BulkOperationResultDto> BulkUpsertUserPermissionsAsync(IReadOnlyCollection<BulkUserPermissionUpsertItem> items, AdminMutationContext context, CancellationToken cancellationToken);
    Task<BanRuleDto> UpsertBanRuleAsync(string scope, string subject, BanRuleUpsertRequest request, AdminMutationContext context, CancellationToken cancellationToken);
    Task<BulkOperationResultDto> BulkUpsertBanRulesAsync(IReadOnlyCollection<BulkBanRuleUpsertItem> items, AdminMutationContext context, CancellationToken cancellationToken);
    Task<BulkOperationResultDto> BulkExpireBanRulesAsync(IReadOnlyCollection<BulkBanRuleExpireItem> items, AdminMutationContext context, CancellationToken cancellationToken);
    Task<BulkOperationResultDto> BulkDeleteBanRulesAsync(IReadOnlyCollection<BulkBanRuleDeleteItem> items, AdminMutationContext context, CancellationToken cancellationToken);
    Task DeleteBanRuleAsync(string scope, string subject, long? expectedVersion, AdminMutationContext context, CancellationToken cancellationToken);
    Task TriggerCacheRefreshAsync(string operation, AdminMutationContext context, CancellationToken cancellationToken);
}

internal sealed class GetClusterOverviewQueryHandler(IClusterOverviewReader reader) : IRequestHandler<GetClusterOverviewQuery, ClusterOverviewDto>
{
    public Task<ClusterOverviewDto> Handle(GetClusterOverviewQuery request, CancellationToken cancellationToken)
        => reader.GetAsync(cancellationToken);
}

internal sealed class ListAuditRecordsQueryHandler(IAuditRecordReader reader) : IRequestHandler<ListAuditRecordsQuery, IReadOnlyCollection<AuditRecordDto>>
{
    public Task<IReadOnlyCollection<AuditRecordDto>> Handle(ListAuditRecordsQuery request, CancellationToken cancellationToken)
        => reader.ListAsync(request.Page, request.PageSize, cancellationToken);
}

internal sealed class ListMaintenanceRunsQueryHandler(IMaintenanceRunReader reader) : IRequestHandler<ListMaintenanceRunsQuery, IReadOnlyCollection<MaintenanceRunDto>>
{
    public Task<IReadOnlyCollection<MaintenanceRunDto>> Handle(ListMaintenanceRunsQuery request, CancellationToken cancellationToken)
        => reader.ListAsync(request.Page, request.PageSize, cancellationToken);
}

internal sealed class ListTorrentsQueryHandler(ITorrentAdminReader reader) : IRequestHandler<ListTorrentsQuery, IReadOnlyCollection<TorrentAdminDto>>
{
    public Task<IReadOnlyCollection<TorrentAdminDto>> Handle(ListTorrentsQuery request, CancellationToken cancellationToken)
        => reader.ListAsync(request.Search, request.IsEnabled, request.IsPrivate, request.Page, request.PageSize, cancellationToken);
}

internal sealed class GetTorrentDetailQueryHandler(ITorrentAdminReader reader) : IRequestHandler<GetTorrentDetailQuery, TorrentAdminDto?>
{
    public Task<TorrentAdminDto?> Handle(GetTorrentDetailQuery request, CancellationToken cancellationToken)
        => reader.GetAsync(request.InfoHash, cancellationToken);
}

internal sealed class ListPasskeysQueryHandler(IPasskeyAdminReader reader) : IRequestHandler<ListPasskeysQuery, IReadOnlyCollection<PasskeyAdminDto>>
{
    public Task<IReadOnlyCollection<PasskeyAdminDto>> Handle(ListPasskeysQuery request, CancellationToken cancellationToken)
        => reader.ListAsync(request.UserId, request.IsRevoked, request.Page, request.PageSize, cancellationToken);
}

internal sealed class ListUserPermissionsQueryHandler(IUserPermissionAdminReader reader) : IRequestHandler<ListUserPermissionsQuery, IReadOnlyCollection<UserPermissionAdminDto>>
{
    public Task<IReadOnlyCollection<UserPermissionAdminDto>> Handle(ListUserPermissionsQuery request, CancellationToken cancellationToken)
        => reader.ListAsync(request.CanUsePrivateTracker, request.Page, request.PageSize, cancellationToken);
}

internal sealed class ListBansQueryHandler(IBanAdminReader reader) : IRequestHandler<ListBansQuery, IReadOnlyCollection<BanRuleAdminDto>>
{
    public Task<IReadOnlyCollection<BanRuleAdminDto>> Handle(ListBansQuery request, CancellationToken cancellationToken)
        => reader.ListAsync(request.Scope, request.Page, request.PageSize, cancellationToken);
}

internal sealed class UpsertTorrentPolicyAdminCommandHandler(IAdminMutationOrchestrator orchestrator) : IRequestHandler<UpsertTorrentPolicyAdminCommand, TorrentPolicyDto>
{
    public Task<TorrentPolicyDto> Handle(UpsertTorrentPolicyAdminCommand request, CancellationToken cancellationToken)
        => orchestrator.UpsertTorrentPolicyAsync(request.InfoHash, request.Request, request.Context, cancellationToken);
}

internal sealed class BulkActivateTorrentsAdminCommandHandler(IAdminMutationOrchestrator orchestrator) : IRequestHandler<BulkActivateTorrentsAdminCommand, BulkOperationResultDto>
{
    public Task<BulkOperationResultDto> Handle(BulkActivateTorrentsAdminCommand request, CancellationToken cancellationToken)
        => orchestrator.BulkActivateTorrentsAsync(request.Items, request.Context, cancellationToken);
}

internal sealed class BulkDeactivateTorrentsAdminCommandHandler(IAdminMutationOrchestrator orchestrator) : IRequestHandler<BulkDeactivateTorrentsAdminCommand, BulkOperationResultDto>
{
    public Task<BulkOperationResultDto> Handle(BulkDeactivateTorrentsAdminCommand request, CancellationToken cancellationToken)
        => orchestrator.BulkDeactivateTorrentsAsync(request.Items, request.Context, cancellationToken);
}

internal sealed class BulkUpsertTorrentPoliciesAdminCommandHandler(IAdminMutationOrchestrator orchestrator) : IRequestHandler<BulkUpsertTorrentPoliciesAdminCommand, BulkOperationResultDto>
{
    public Task<BulkOperationResultDto> Handle(BulkUpsertTorrentPoliciesAdminCommand request, CancellationToken cancellationToken)
        => orchestrator.BulkUpsertTorrentPoliciesAsync(request.Items, request.Context, cancellationToken);
}

internal sealed class DryRunBulkUpsertTorrentPoliciesAdminCommandHandler(IAdminMutationOrchestrator orchestrator) : IRequestHandler<DryRunBulkUpsertTorrentPoliciesAdminCommand, BulkDryRunResultDto>
{
    public Task<BulkDryRunResultDto> Handle(DryRunBulkUpsertTorrentPoliciesAdminCommand request, CancellationToken cancellationToken)
        => orchestrator.DryRunBulkUpsertTorrentPoliciesAsync(request.Items, cancellationToken);
}

internal sealed class UpsertPasskeyAdminCommandHandler(IAdminMutationOrchestrator orchestrator) : IRequestHandler<UpsertPasskeyAdminCommand, PasskeyAccessDto>
{
    public Task<PasskeyAccessDto> Handle(UpsertPasskeyAdminCommand request, CancellationToken cancellationToken)
        => orchestrator.UpsertPasskeyAsync(request.Passkey, request.Request, request.Context, cancellationToken);
}

internal sealed class BulkRevokePasskeysAdminCommandHandler(IAdminMutationOrchestrator orchestrator) : IRequestHandler<BulkRevokePasskeysAdminCommand, BulkOperationResultDto>
{
    public Task<BulkOperationResultDto> Handle(BulkRevokePasskeysAdminCommand request, CancellationToken cancellationToken)
        => orchestrator.BulkRevokePasskeysAsync(request.Items, request.Context, cancellationToken);
}

internal sealed class BulkRotatePasskeysAdminCommandHandler(IAdminMutationOrchestrator orchestrator) : IRequestHandler<BulkRotatePasskeysAdminCommand, BulkOperationResultDto>
{
    public Task<BulkOperationResultDto> Handle(BulkRotatePasskeysAdminCommand request, CancellationToken cancellationToken)
        => orchestrator.BulkRotatePasskeysAsync(request.Items, request.Context, cancellationToken);
}

internal sealed class UpsertUserPermissionsAdminCommandHandler(IAdminMutationOrchestrator orchestrator) : IRequestHandler<UpsertUserPermissionsAdminCommand, UserPermissionSnapshotDto>
{
    public Task<UserPermissionSnapshotDto> Handle(UpsertUserPermissionsAdminCommand request, CancellationToken cancellationToken)
        => orchestrator.UpsertUserPermissionsAsync(request.UserId, request.Request, request.Context, cancellationToken);
}

internal sealed class BulkUpsertUserPermissionsAdminCommandHandler(IAdminMutationOrchestrator orchestrator) : IRequestHandler<BulkUpsertUserPermissionsAdminCommand, BulkOperationResultDto>
{
    public Task<BulkOperationResultDto> Handle(BulkUpsertUserPermissionsAdminCommand request, CancellationToken cancellationToken)
        => orchestrator.BulkUpsertUserPermissionsAsync(request.Items, request.Context, cancellationToken);
}

internal sealed class UpsertBanAdminCommandHandler(IAdminMutationOrchestrator orchestrator) : IRequestHandler<UpsertBanAdminCommand, BanRuleDto>
{
    public Task<BanRuleDto> Handle(UpsertBanAdminCommand request, CancellationToken cancellationToken)
        => orchestrator.UpsertBanRuleAsync(request.Scope, request.Subject, request.Request, request.Context, cancellationToken);
}

internal sealed class BulkUpsertBansAdminCommandHandler(IAdminMutationOrchestrator orchestrator) : IRequestHandler<BulkUpsertBansAdminCommand, BulkOperationResultDto>
{
    public Task<BulkOperationResultDto> Handle(BulkUpsertBansAdminCommand request, CancellationToken cancellationToken)
        => orchestrator.BulkUpsertBanRulesAsync(request.Items, request.Context, cancellationToken);
}

internal sealed class BulkExpireBansAdminCommandHandler(IAdminMutationOrchestrator orchestrator) : IRequestHandler<BulkExpireBansAdminCommand, BulkOperationResultDto>
{
    public Task<BulkOperationResultDto> Handle(BulkExpireBansAdminCommand request, CancellationToken cancellationToken)
        => orchestrator.BulkExpireBanRulesAsync(request.Items, request.Context, cancellationToken);
}

internal sealed class BulkDeleteBansAdminCommandHandler(IAdminMutationOrchestrator orchestrator) : IRequestHandler<BulkDeleteBansAdminCommand, BulkOperationResultDto>
{
    public Task<BulkOperationResultDto> Handle(BulkDeleteBansAdminCommand request, CancellationToken cancellationToken)
        => orchestrator.BulkDeleteBanRulesAsync(request.Items, request.Context, cancellationToken);
}

internal sealed class DeleteBanAdminCommandHandler(IAdminMutationOrchestrator orchestrator) : IRequestHandler<DeleteBanAdminCommand, Unit>
{
    public async Task<Unit> Handle(DeleteBanAdminCommand request, CancellationToken cancellationToken)
    {
        await orchestrator.DeleteBanRuleAsync(request.Scope, request.Subject, request.ExpectedVersion, request.Context, cancellationToken);
        return Unit.Value;
    }
}

internal sealed class TriggerCacheRefreshAdminCommandHandler(IAdminMutationOrchestrator orchestrator) : IRequestHandler<TriggerCacheRefreshAdminCommand, Unit>
{
    public async Task<Unit> Handle(TriggerCacheRefreshAdminCommand request, CancellationToken cancellationToken)
    {
        await orchestrator.TriggerCacheRefreshAsync(request.Operation, request.Context, cancellationToken);
        return Unit.Value;
    }
}

public static class AdminApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddAdminApplication(this IServiceCollection services)
    {
        services.AddMediatR(typeof(AdminApplicationServiceCollectionExtensions).Assembly);
        return services;
    }
}
