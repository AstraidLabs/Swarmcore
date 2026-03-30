using MediatR;
using Microsoft.Extensions.DependencyInjection;
using BeeTracker.BuildingBlocks.Application.Queries;
using BeeTracker.Contracts.Admin;
using BeeTracker.Contracts.Configuration;
using BeeTracker.Contracts.Runtime;

namespace Tracker.AdminService.Application;

public enum AuditRecordFilter
{
    All,
    Success,
    Failure,
    Warn,
    Identity,
    Policy,
    Node,
    Mail,
    Access,
    Torrent
}

public enum MaintenanceRunFilter
{
    All,
    Completed,
    Failed,
    Running
}

public enum TorrentCatalogFilter
{
    All,
    Enabled,
    Disabled,
    Private,
    Public
}

public enum PasskeyCatalogFilter
{
    All,
    Active,
    Revoked,
    Expired
}

public enum TrackerAccessRightsFilter
{
    All,
    Private,
    Public,
    Seed,
    Leech,
    Scrape
}

public enum BanCatalogFilter
{
    All,
    Active,
    Expired
}

public sealed record GetClusterOverviewQuery() : IRequest<ClusterOverviewDto>;

public sealed record ListAuditRecordsQuery(GridQuery Query, AuditRecordFilter Filter) : IRequest<PageResult<AuditRecordDto>>;

public sealed record ListMaintenanceRunsQuery(GridQuery Query, MaintenanceRunFilter Filter) : IRequest<PageResult<MaintenanceRunDto>>;

public sealed record ListTorrentsQuery(GridQuery Query, TorrentCatalogFilter Filter, bool? IsEnabled = null, bool? IsPrivate = null)
    : IRequest<PageResult<TorrentAdminDto>>;

public sealed record GetTorrentDetailQuery(string InfoHash) : IRequest<TorrentAdminDto?>;

public sealed record ListPasskeysQuery(GridQuery Query, PasskeyCatalogFilter Filter, Guid? UserId = null, bool? IsRevoked = null)
    : IRequest<PageResult<PasskeyAdminDto>>;

public sealed record GetPasskeyDetailQuery(Guid Id) : IRequest<PasskeyAdminDto?>;

public sealed record ListTrackerAccessRightsQuery(GridQuery Query, TrackerAccessRightsFilter Filter, bool? CanUsePrivateTracker = null)
    : IRequest<PageResult<TrackerAccessAdminDto>>;

public sealed record ListBansQuery(GridQuery Query, BanCatalogFilter Filter, string? Scope = null)
    : IRequest<PageResult<BanRuleAdminDto>>;

public sealed record GetTrackerAccessRightQuery(Guid UserId) : IRequest<TrackerAccessAdminDto?>;

public sealed record GetBanRuleQuery(string Scope, string Subject) : IRequest<BanRuleAdminDto?>;

public sealed record ListTrackerNodeConfigsQuery(GridQuery Query) : IRequest<PageResult<TrackerNodeCatalogItemDto>>;

public sealed record GetTrackerNodeConfigViewQuery(string NodeKey) : IRequest<TrackerNodeConfigViewDto?>;

public sealed record UpsertTorrentPolicyAdminCommand(string InfoHash, TorrentPolicyUpsertRequest Request, AdminMutationContext Context)
    : IRequest<TorrentPolicyDto>;

public sealed record DeleteTorrentAdminCommand(string InfoHash, long? ExpectedVersion, AdminMutationContext Context)
    : IRequest<Unit>;

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

public sealed record CreatePasskeyAdminCommand(PasskeyCreateRequest Request, AdminMutationContext Context)
    : IRequest<PasskeyAccessDto>;

public sealed record UpsertPasskeyByIdAdminCommand(Guid Id, PasskeyUpsertRequest Request, AdminMutationContext Context)
    : IRequest<PasskeyAccessDto>;

public sealed record RevokePasskeyByIdAdminCommand(Guid Id, PasskeyRevokeRequest Request, AdminMutationContext Context)
    : IRequest<PasskeyAccessDto>;

public sealed record RotatePasskeyByIdAdminCommand(Guid Id, PasskeyRotateRequest Request, AdminMutationContext Context)
    : IRequest<(PasskeyAccessDto RevokedSnapshot, PasskeyAccessDto NewSnapshot)>;

public sealed record DeletePasskeyAdminCommand(Guid Id, long? ExpectedVersion, AdminMutationContext Context)
    : IRequest<Unit>;

public sealed record BulkRevokePasskeysAdminCommand(IReadOnlyCollection<BulkPasskeyRevokeItem> Items, AdminMutationContext Context)
    : IRequest<BulkOperationResultDto>;

public sealed record BulkRotatePasskeysAdminCommand(IReadOnlyCollection<BulkPasskeyRotateItem> Items, AdminMutationContext Context)
    : IRequest<BulkOperationResultDto>;

public sealed record UpsertTrackerAccessRightsAdminCommand(Guid UserId, TrackerAccessRightsUpsertRequest Request, AdminMutationContext Context)
    : IRequest<TrackerAccessRightsDto>;

public sealed record DeleteTrackerAccessRightsAdminCommand(Guid UserId, long? ExpectedVersion, AdminMutationContext Context)
    : IRequest<Unit>;

public sealed record BulkUpsertTrackerAccessRightsAdminCommand(IReadOnlyCollection<BulkTrackerAccessRightsUpsertItem> Items, AdminMutationContext Context)
    : IRequest<BulkOperationResultDto>;

public sealed record UpsertBanAdminCommand(string Scope, string Subject, BanRuleUpsertRequest Request, AdminMutationContext Context)
    : IRequest<BanRuleDto>;

public sealed record BulkUpsertBansAdminCommand(IReadOnlyCollection<BulkBanRuleUpsertItem> Items, AdminMutationContext Context)
    : IRequest<BulkOperationResultDto>;

public sealed record BulkExpireBansAdminCommand(IReadOnlyCollection<BulkBanRuleExpireItem> Items, AdminMutationContext Context)
    : IRequest<BulkOperationResultDto>;

public sealed record BulkDeleteBansAdminCommand(IReadOnlyCollection<BulkBanRuleDeleteItem> Items, AdminMutationContext Context)
    : IRequest<BulkOperationResultDto>;

public sealed record ExpireBanAdminCommand(string Scope, string Subject, BanRuleExpireRequest Request, AdminMutationContext Context)
    : IRequest<BanRuleDto>;

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
    Task<PageResult<AuditRecordDto>> ListAsync(GridQuery query, AuditRecordFilter filter, CancellationToken cancellationToken);
}

public interface IMaintenanceRunReader
{
    Task<PageResult<MaintenanceRunDto>> ListAsync(GridQuery query, MaintenanceRunFilter filter, CancellationToken cancellationToken);
}

public interface ITorrentAdminReader
{
    Task<PageResult<TorrentAdminDto>> ListAsync(GridQuery query, TorrentCatalogFilter filter, bool? isEnabled, bool? isPrivate, CancellationToken cancellationToken);
    Task<TorrentAdminDto?> GetAsync(string infoHash, CancellationToken cancellationToken);
}

public interface IPasskeyAdminReader
{
    Task<PageResult<PasskeyAdminDto>> ListAsync(GridQuery query, PasskeyCatalogFilter filter, Guid? userId, bool? isRevoked, CancellationToken cancellationToken);
    Task<PasskeyAdminDto?> GetAsync(Guid id, CancellationToken cancellationToken);
}

public interface ITrackerAccessRightsAdminReader
{
    Task<PageResult<TrackerAccessAdminDto>> ListAsync(GridQuery query, TrackerAccessRightsFilter filter, bool? canUsePrivateTracker, CancellationToken cancellationToken);
    Task<TrackerAccessAdminDto?> GetAsync(Guid userId, CancellationToken cancellationToken);
}

public interface IBanAdminReader
{
    Task<PageResult<BanRuleAdminDto>> ListAsync(GridQuery query, BanCatalogFilter filter, string? scope, CancellationToken cancellationToken);
    Task<BanRuleAdminDto?> GetAsync(string scope, string subject, CancellationToken cancellationToken);
}

public interface ITrackerNodeConfigAdminReader
{
    Task<PageResult<TrackerNodeCatalogItemDto>> ListAsync(GridQuery query, CancellationToken cancellationToken);
    Task<TrackerNodeConfigViewDto?> GetAsync(string nodeKey, CancellationToken cancellationToken);
}

// ─── Gateway Admin Proxy ─────────────────────────────────────────────────────

/// <summary>
/// Proxies administrative operations to individual Tracker.Gateway instances.
/// Resolves gateway HTTP addresses from persisted node configurations.
/// </summary>
public interface IGatewayAdminClient
{
    Task<GovernanceStateDto?> GetGovernanceAsync(string nodeKey, CancellationToken cancellationToken);
    Task<GovernanceStateDto?> UpdateGovernanceAsync(string nodeKey, GovernanceUpdateRequest request, CancellationToken cancellationToken);
    Task<RuntimeDiagnosticsDto?> GetDiagnosticsAsync(string nodeKey, CancellationToken cancellationToken);
    Task<AbuseDiagnosticsDto?> GetAbuseDiagnosticsAsync(string nodeKey, CancellationToken cancellationToken);
    Task<TrackerOverviewDto?> GetOverviewAsync(string nodeKey, CancellationToken cancellationToken);
    Task<NodeOperationalStateDto?> GetNodeStateAsync(string nodeKey, string nodeId, CancellationToken cancellationToken);
    Task<NodeOperationalStateDto?> DrainNodeAsync(string nodeKey, string nodeId, CancellationToken cancellationToken);
    Task<NodeOperationalStateDto?> SetMaintenanceAsync(string nodeKey, string nodeId, CancellationToken cancellationToken);
    Task<NodeOperationalStateDto?> ActivateNodeAsync(string nodeKey, string nodeId, CancellationToken cancellationToken);
}

// ─── Notification Outbox Admin Reader ───────────────────────────────────────

public interface INotificationAdminReader
{
    Task<PageResult<NotificationOutboxItemDto>> ListAsync(GridQuery query, CancellationToken cancellationToken);
    Task<NotificationOutboxDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<NotificationOutboxStatsDto> GetStatsAsync(CancellationToken cancellationToken);
    Task<bool> RetryAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> CancelAsync(Guid id, CancellationToken cancellationToken);
}

public interface IAdminMutationOrchestrator
{
    Task<TrackerNodeConfigurationDto> UpsertTrackerNodeConfigurationAsync(string nodeKey, TrackerNodeConfigurationUpsertRequest request, AdminMutationContext context, CancellationToken cancellationToken);
    Task DeleteTrackerNodeConfigurationAsync(string nodeKey, long? expectedVersion, AdminMutationContext context, CancellationToken cancellationToken);
    Task<TorrentPolicyDto> UpsertTorrentPolicyAsync(string infoHash, TorrentPolicyUpsertRequest request, AdminMutationContext context, CancellationToken cancellationToken);
    Task DeleteTorrentAsync(string infoHash, long? expectedVersion, AdminMutationContext context, CancellationToken cancellationToken);
    Task<BulkOperationResultDto> BulkActivateTorrentsAsync(IReadOnlyCollection<BulkTorrentActivationItem> items, AdminMutationContext context, CancellationToken cancellationToken);
    Task<BulkOperationResultDto> BulkDeactivateTorrentsAsync(IReadOnlyCollection<BulkTorrentActivationItem> items, AdminMutationContext context, CancellationToken cancellationToken);
    Task<BulkOperationResultDto> BulkUpsertTorrentPoliciesAsync(IReadOnlyCollection<BulkTorrentPolicyUpsertItem> items, AdminMutationContext context, CancellationToken cancellationToken);
    Task<BulkDryRunResultDto> DryRunBulkUpsertTorrentPoliciesAsync(IReadOnlyCollection<BulkTorrentPolicyUpsertItem> items, CancellationToken cancellationToken);
    Task<PasskeyAccessDto> UpsertPasskeyAsync(string passkey, PasskeyUpsertRequest request, AdminMutationContext context, CancellationToken cancellationToken);
    Task<PasskeyAccessDto> CreatePasskeyAsync(PasskeyCreateRequest request, AdminMutationContext context, CancellationToken cancellationToken);
    Task<PasskeyAccessDto> UpsertPasskeyByIdAsync(Guid id, PasskeyUpsertRequest request, AdminMutationContext context, CancellationToken cancellationToken);
    Task<PasskeyAccessDto> RevokePasskeyByIdAsync(Guid id, PasskeyRevokeRequest request, AdminMutationContext context, CancellationToken cancellationToken);
    Task<(PasskeyAccessDto RevokedSnapshot, PasskeyAccessDto NewSnapshot)> RotatePasskeyByIdAsync(Guid id, PasskeyRotateRequest request, AdminMutationContext context, CancellationToken cancellationToken);
    Task DeletePasskeyByIdAsync(Guid id, long? expectedVersion, AdminMutationContext context, CancellationToken cancellationToken);
    Task<BulkOperationResultDto> BulkRevokePasskeysAsync(IReadOnlyCollection<BulkPasskeyRevokeItem> items, AdminMutationContext context, CancellationToken cancellationToken);
    Task<BulkOperationResultDto> BulkRotatePasskeysAsync(IReadOnlyCollection<BulkPasskeyRotateItem> items, AdminMutationContext context, CancellationToken cancellationToken);
    Task<TrackerAccessRightsDto> UpsertTrackerAccessRightsAsync(Guid userId, TrackerAccessRightsUpsertRequest request, AdminMutationContext context, CancellationToken cancellationToken);
    Task DeleteTrackerAccessRightsAsync(Guid userId, long? expectedVersion, AdminMutationContext context, CancellationToken cancellationToken);
    Task<BulkOperationResultDto> BulkUpsertTrackerAccessRightsAsync(IReadOnlyCollection<BulkTrackerAccessRightsUpsertItem> items, AdminMutationContext context, CancellationToken cancellationToken);
    Task<BanRuleDto> UpsertBanRuleAsync(string scope, string subject, BanRuleUpsertRequest request, AdminMutationContext context, CancellationToken cancellationToken);
    Task<BanRuleDto> ExpireBanRuleAsync(string scope, string subject, BanRuleExpireRequest request, AdminMutationContext context, CancellationToken cancellationToken);
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

internal sealed class ListAuditRecordsQueryHandler(IAuditRecordReader reader) : IRequestHandler<ListAuditRecordsQuery, PageResult<AuditRecordDto>>
{
    public Task<PageResult<AuditRecordDto>> Handle(ListAuditRecordsQuery request, CancellationToken cancellationToken)
        => reader.ListAsync(request.Query, request.Filter, cancellationToken);
}

internal sealed class ListMaintenanceRunsQueryHandler(IMaintenanceRunReader reader) : IRequestHandler<ListMaintenanceRunsQuery, PageResult<MaintenanceRunDto>>
{
    public Task<PageResult<MaintenanceRunDto>> Handle(ListMaintenanceRunsQuery request, CancellationToken cancellationToken)
        => reader.ListAsync(request.Query, request.Filter, cancellationToken);
}

internal sealed class ListTorrentsQueryHandler(ITorrentAdminReader reader) : IRequestHandler<ListTorrentsQuery, PageResult<TorrentAdminDto>>
{
    public Task<PageResult<TorrentAdminDto>> Handle(ListTorrentsQuery request, CancellationToken cancellationToken)
        => reader.ListAsync(request.Query, request.Filter, request.IsEnabled, request.IsPrivate, cancellationToken);
}

internal sealed class GetTorrentDetailQueryHandler(ITorrentAdminReader reader) : IRequestHandler<GetTorrentDetailQuery, TorrentAdminDto?>
{
    public Task<TorrentAdminDto?> Handle(GetTorrentDetailQuery request, CancellationToken cancellationToken)
        => reader.GetAsync(request.InfoHash, cancellationToken);
}

internal sealed class ListPasskeysQueryHandler(IPasskeyAdminReader reader) : IRequestHandler<ListPasskeysQuery, PageResult<PasskeyAdminDto>>
{
    public Task<PageResult<PasskeyAdminDto>> Handle(ListPasskeysQuery request, CancellationToken cancellationToken)
        => reader.ListAsync(request.Query, request.Filter, request.UserId, request.IsRevoked, cancellationToken);
}

internal sealed class GetPasskeyDetailQueryHandler(IPasskeyAdminReader reader) : IRequestHandler<GetPasskeyDetailQuery, PasskeyAdminDto?>
{
    public Task<PasskeyAdminDto?> Handle(GetPasskeyDetailQuery request, CancellationToken cancellationToken)
        => reader.GetAsync(request.Id, cancellationToken);
}

internal sealed class ListTrackerAccessRightsQueryHandler(ITrackerAccessRightsAdminReader reader) : IRequestHandler<ListTrackerAccessRightsQuery, PageResult<TrackerAccessAdminDto>>
{
    public Task<PageResult<TrackerAccessAdminDto>> Handle(ListTrackerAccessRightsQuery request, CancellationToken cancellationToken)
        => reader.ListAsync(request.Query, request.Filter, request.CanUsePrivateTracker, cancellationToken);
}

internal sealed class ListBansQueryHandler(IBanAdminReader reader) : IRequestHandler<ListBansQuery, PageResult<BanRuleAdminDto>>
{
    public Task<PageResult<BanRuleAdminDto>> Handle(ListBansQuery request, CancellationToken cancellationToken)
        => reader.ListAsync(request.Query, request.Filter, request.Scope, cancellationToken);
}

internal sealed class GetTrackerAccessRightQueryHandler(ITrackerAccessRightsAdminReader reader) : IRequestHandler<GetTrackerAccessRightQuery, TrackerAccessAdminDto?>
{
    public Task<TrackerAccessAdminDto?> Handle(GetTrackerAccessRightQuery request, CancellationToken cancellationToken)
        => reader.GetAsync(request.UserId, cancellationToken);
}

internal sealed class GetBanRuleQueryHandler(IBanAdminReader reader) : IRequestHandler<GetBanRuleQuery, BanRuleAdminDto?>
{
    public Task<BanRuleAdminDto?> Handle(GetBanRuleQuery request, CancellationToken cancellationToken)
        => reader.GetAsync(request.Scope, request.Subject, cancellationToken);
}

internal sealed class ListTrackerNodeConfigsQueryHandler(ITrackerNodeConfigAdminReader reader) : IRequestHandler<ListTrackerNodeConfigsQuery, PageResult<TrackerNodeCatalogItemDto>>
{
    public Task<PageResult<TrackerNodeCatalogItemDto>> Handle(ListTrackerNodeConfigsQuery request, CancellationToken cancellationToken)
        => reader.ListAsync(request.Query, cancellationToken);
}

internal sealed class GetTrackerNodeConfigViewQueryHandler(ITrackerNodeConfigAdminReader reader) : IRequestHandler<GetTrackerNodeConfigViewQuery, TrackerNodeConfigViewDto?>
{
    public Task<TrackerNodeConfigViewDto?> Handle(GetTrackerNodeConfigViewQuery request, CancellationToken cancellationToken)
        => reader.GetAsync(request.NodeKey, cancellationToken);
}

internal sealed class UpsertTorrentPolicyAdminCommandHandler(IAdminMutationOrchestrator orchestrator) : IRequestHandler<UpsertTorrentPolicyAdminCommand, TorrentPolicyDto>
{
    public Task<TorrentPolicyDto> Handle(UpsertTorrentPolicyAdminCommand request, CancellationToken cancellationToken)
        => orchestrator.UpsertTorrentPolicyAsync(request.InfoHash, request.Request, request.Context, cancellationToken);
}

internal sealed class DeleteTorrentAdminCommandHandler(IAdminMutationOrchestrator orchestrator) : IRequestHandler<DeleteTorrentAdminCommand, Unit>
{
    public async Task<Unit> Handle(DeleteTorrentAdminCommand request, CancellationToken cancellationToken)
    {
        await orchestrator.DeleteTorrentAsync(request.InfoHash, request.ExpectedVersion, request.Context, cancellationToken);
        return Unit.Value;
    }
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

internal sealed class CreatePasskeyAdminCommandHandler(IAdminMutationOrchestrator orchestrator) : IRequestHandler<CreatePasskeyAdminCommand, PasskeyAccessDto>
{
    public Task<PasskeyAccessDto> Handle(CreatePasskeyAdminCommand request, CancellationToken cancellationToken)
        => orchestrator.CreatePasskeyAsync(request.Request, request.Context, cancellationToken);
}

internal sealed class UpsertPasskeyByIdAdminCommandHandler(IAdminMutationOrchestrator orchestrator) : IRequestHandler<UpsertPasskeyByIdAdminCommand, PasskeyAccessDto>
{
    public Task<PasskeyAccessDto> Handle(UpsertPasskeyByIdAdminCommand request, CancellationToken cancellationToken)
        => orchestrator.UpsertPasskeyByIdAsync(request.Id, request.Request, request.Context, cancellationToken);
}

internal sealed class RevokePasskeyByIdAdminCommandHandler(IAdminMutationOrchestrator orchestrator) : IRequestHandler<RevokePasskeyByIdAdminCommand, PasskeyAccessDto>
{
    public Task<PasskeyAccessDto> Handle(RevokePasskeyByIdAdminCommand request, CancellationToken cancellationToken)
        => orchestrator.RevokePasskeyByIdAsync(request.Id, request.Request, request.Context, cancellationToken);
}

internal sealed class RotatePasskeyByIdAdminCommandHandler(IAdminMutationOrchestrator orchestrator) : IRequestHandler<RotatePasskeyByIdAdminCommand, (PasskeyAccessDto RevokedSnapshot, PasskeyAccessDto NewSnapshot)>
{
    public Task<(PasskeyAccessDto RevokedSnapshot, PasskeyAccessDto NewSnapshot)> Handle(RotatePasskeyByIdAdminCommand request, CancellationToken cancellationToken)
        => orchestrator.RotatePasskeyByIdAsync(request.Id, request.Request, request.Context, cancellationToken);
}

internal sealed class DeletePasskeyAdminCommandHandler(IAdminMutationOrchestrator orchestrator) : IRequestHandler<DeletePasskeyAdminCommand, Unit>
{
    public async Task<Unit> Handle(DeletePasskeyAdminCommand request, CancellationToken cancellationToken)
    {
        await orchestrator.DeletePasskeyByIdAsync(request.Id, request.ExpectedVersion, request.Context, cancellationToken);
        return Unit.Value;
    }
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

internal sealed class UpsertTrackerAccessRightsAdminCommandHandler(IAdminMutationOrchestrator orchestrator) : IRequestHandler<UpsertTrackerAccessRightsAdminCommand, TrackerAccessRightsDto>
{
    public Task<TrackerAccessRightsDto> Handle(UpsertTrackerAccessRightsAdminCommand request, CancellationToken cancellationToken)
        => orchestrator.UpsertTrackerAccessRightsAsync(request.UserId, request.Request, request.Context, cancellationToken);
}

internal sealed class DeleteTrackerAccessRightsAdminCommandHandler(IAdminMutationOrchestrator orchestrator) : IRequestHandler<DeleteTrackerAccessRightsAdminCommand, Unit>
{
    public async Task<Unit> Handle(DeleteTrackerAccessRightsAdminCommand request, CancellationToken cancellationToken)
    {
        await orchestrator.DeleteTrackerAccessRightsAsync(request.UserId, request.ExpectedVersion, request.Context, cancellationToken);
        return Unit.Value;
    }
}

internal sealed class BulkUpsertTrackerAccessRightsAdminCommandHandler(IAdminMutationOrchestrator orchestrator) : IRequestHandler<BulkUpsertTrackerAccessRightsAdminCommand, BulkOperationResultDto>
{
    public Task<BulkOperationResultDto> Handle(BulkUpsertTrackerAccessRightsAdminCommand request, CancellationToken cancellationToken)
        => orchestrator.BulkUpsertTrackerAccessRightsAsync(request.Items, request.Context, cancellationToken);
}

internal sealed class UpsertBanAdminCommandHandler(IAdminMutationOrchestrator orchestrator) : IRequestHandler<UpsertBanAdminCommand, BanRuleDto>
{
    public Task<BanRuleDto> Handle(UpsertBanAdminCommand request, CancellationToken cancellationToken)
        => orchestrator.UpsertBanRuleAsync(request.Scope, request.Subject, request.Request, request.Context, cancellationToken);
}

internal sealed class ExpireBanAdminCommandHandler(IAdminMutationOrchestrator orchestrator) : IRequestHandler<ExpireBanAdminCommand, BanRuleDto>
{
    public Task<BanRuleDto> Handle(ExpireBanAdminCommand request, CancellationToken cancellationToken)
        => orchestrator.ExpireBanRuleAsync(request.Scope, request.Subject, request.Request, request.Context, cancellationToken);
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
