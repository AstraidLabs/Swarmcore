using Swarmcore.Contracts.Configuration;

namespace Tracker.ConfigurationService.Application;

public sealed class ConfigurationConcurrencyException(string entityType, string entityKey, long expectedVersion, long actualVersion)
    : Exception($"Concurrency conflict for {entityType} '{entityKey}'. Expected version {expectedVersion}, actual version {actualVersion}.")
{
    public string EntityType { get; } = entityType;
    public string EntityKey { get; } = entityKey;
    public long ExpectedVersion { get; } = expectedVersion;
    public long ActualVersion { get; } = actualVersion;
}

public sealed class ConfigurationEntityNotFoundException(string entityType, string entityKey)
    : Exception($"{entityType} '{entityKey}' was not found.")
{
    public string EntityType { get; } = entityType;
    public string EntityKey { get; } = entityKey;
}

public interface ITorrentConfigurationReader
{
    Task<IReadOnlyCollection<TorrentPolicyDto>> GetTorrentPoliciesAsync(CancellationToken cancellationToken);
}

public interface IConfigurationMutationService
{
    Task<TorrentPolicyDto> UpsertTorrentPolicyAsync(string infoHash, TorrentPolicyUpsertRequest request, AdminMutationContext context, CancellationToken cancellationToken);
    Task<TorrentPolicyDto> ActivateTorrentAsync(string infoHash, TorrentActivationRequest request, AdminMutationContext context, CancellationToken cancellationToken);
    Task<TorrentPolicyDto> DeactivateTorrentAsync(string infoHash, TorrentActivationRequest request, AdminMutationContext context, CancellationToken cancellationToken);
    Task<PasskeyAccessDto> UpsertPasskeyAsync(string passkey, PasskeyUpsertRequest request, AdminMutationContext context, CancellationToken cancellationToken);
    Task<PasskeyAccessDto> RevokePasskeyAsync(string passkey, PasskeyRevokeRequest request, AdminMutationContext context, CancellationToken cancellationToken);
    Task<(PasskeyAccessDto RevokedSnapshot, PasskeyAccessDto NewSnapshot)> RotatePasskeyAsync(string passkey, PasskeyRotateRequest request, AdminMutationContext context, CancellationToken cancellationToken);
    Task<UserPermissionSnapshotDto> UpsertUserPermissionsAsync(Guid userId, UserPermissionUpsertRequest request, AdminMutationContext context, CancellationToken cancellationToken);
    Task<BanRuleDto> UpsertBanRuleAsync(string scope, string subject, BanRuleUpsertRequest request, AdminMutationContext context, CancellationToken cancellationToken);
    Task<BanRuleDto> ExpireBanRuleAsync(string scope, string subject, BanRuleExpireRequest request, AdminMutationContext context, CancellationToken cancellationToken);
    Task DeleteBanRuleAsync(string scope, string subject, long? expectedVersion, AdminMutationContext context, CancellationToken cancellationToken);
}

public interface IConfigurationMutationPreviewService
{
    Task<TorrentPolicyMutationPreviewDto> PreviewUpsertTorrentPolicyAsync(string infoHash, TorrentPolicyUpsertRequest request, CancellationToken cancellationToken);
}

public interface IConfigurationCacheInvalidationPublisher
{
    Task PublishTorrentPolicyInvalidationAsync(string infoHashHex, CancellationToken cancellationToken);
    Task PublishPasskeyInvalidationAsync(string passkey, CancellationToken cancellationToken);
    Task PublishUserPermissionInvalidationAsync(Guid userId, CancellationToken cancellationToken);
    Task PublishBanRuleInvalidationAsync(string scope, string subject, CancellationToken cancellationToken);
}

public interface IConfigurationMaintenanceService
{
    Task TriggerCacheRefreshAsync(string operation, AdminMutationContext context, CancellationToken cancellationToken);
}
