using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.ComponentModel.DataAnnotations;
using BeeTracker.Contracts.Configuration;
using Tracker.ConfigurationService.Application;

namespace Tracker.ConfigurationService.Infrastructure;

internal sealed class EfConfigurationMutationService(
    TrackerConfigurationDbContext dbContext,
    IConfigurationCacheInvalidationPublisher invalidationPublisher,
    IAuditBuffer auditBuffer) : IConfigurationMutationService, IConfigurationMutationPreviewService
{
    private static readonly System.Text.Json.JsonSerializerOptions AuditJsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    private sealed record TorrentPolicyCurrentState(
        bool TorrentExists,
        TorrentPolicyDto? CurrentSnapshot,
        Guid TorrentId,
        long CurrentVersion);

    private static string MaskPasskey(string passkey)
    {
        if (string.IsNullOrWhiteSpace(passkey))
        {
            return "pk:masked";
        }

        return passkey.Length <= 6
            ? $"pk:{passkey[0]}...{passkey[^1]}"
            : $"pk:{passkey[..4]}...{passkey[^2..]}";
    }

    private static string GeneratePasskey()
        => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(20)).ToLowerInvariant();

    public async Task<TorrentPolicyMutationPreviewDto> PreviewUpsertTorrentPolicyAsync(string infoHash, TorrentPolicyUpsertRequest request, CancellationToken cancellationToken)
    {
        var normalizedInfoHash = infoHash.ToUpperInvariant();
        var currentState = await LoadTorrentPolicyCurrentStateAsync(normalizedInfoHash, cancellationToken);
        var warnings = BuildTorrentPolicyPreviewWarnings(currentState);

        try
        {
            ValidateTorrentPolicyExpectedVersion(normalizedInfoHash, request.ExpectedVersion, currentState.CurrentVersion);
        }
        catch (ConfigurationConcurrencyException exception)
        {
            return new TorrentPolicyMutationPreviewDto(
                false,
                "concurrency_conflict",
                exception.Message,
                currentState.CurrentSnapshot,
                BuildProposedTorrentPolicy(normalizedInfoHash, request, currentState.CurrentVersion + 1),
                warnings);
        }

        return new TorrentPolicyMutationPreviewDto(
            true,
            null,
            null,
            currentState.CurrentSnapshot,
            BuildProposedTorrentPolicy(normalizedInfoHash, request, currentState.CurrentVersion + 1),
            warnings);
    }

    public async Task<TorrentPolicyDto> UpsertTorrentPolicyAsync(string infoHash, TorrentPolicyUpsertRequest request, AdminMutationContext context, CancellationToken cancellationToken)
    {
        var normalizedInfoHash = infoHash.ToUpperInvariant();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var currentState = await LoadTorrentPolicyCurrentStateAsync(normalizedInfoHash, cancellationToken);
        ValidateTorrentPolicyExpectedVersion(normalizedInfoHash, request.ExpectedVersion, currentState.CurrentVersion);

        var torrent = await EnsureTorrentAsync(currentState, normalizedInfoHash, request.IsPrivate, request.IsEnabled, cancellationToken);
        var nextVersion = currentState.CurrentVersion + 1;
        var policyEntity = await dbContext.TorrentPolicies.SingleOrDefaultAsync(
            policy => policy.TorrentId == torrent.Id,
            cancellationToken);

        if (policyEntity is null)
        {
            policyEntity = new TorrentPolicyEntity
            {
                TorrentId = torrent.Id
            };
            dbContext.TorrentPolicies.Add(policyEntity);
        }

        policyEntity.AnnounceIntervalSeconds = request.AnnounceIntervalSeconds;
        policyEntity.MinAnnounceIntervalSeconds = request.MinAnnounceIntervalSeconds;
        policyEntity.DefaultNumWant = request.DefaultNumWant;
        policyEntity.MaxNumWant = request.MaxNumWant;
        policyEntity.AllowScrape = request.AllowScrape;
        policyEntity.WarningMessage = request.WarningMessage;
        policyEntity.RowVersion = nextVersion;

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await invalidationPublisher.PublishTorrentPolicyInvalidationAsync(normalizedInfoHash, cancellationToken);
        await EnqueueAuditAsync(
            context,
            "torrent_policy.upsert",
            "torrent",
            normalizedInfoHash,
            System.Text.Json.JsonSerializer.Serialize(request, AuditJsonOptions),
            cancellationToken);

        return BuildProposedTorrentPolicy(normalizedInfoHash, request, nextVersion);
    }

    public Task<TorrentPolicyDto> ActivateTorrentAsync(string infoHash, TorrentActivationRequest request, AdminMutationContext context, CancellationToken cancellationToken)
        => SetTorrentEnabledAsync(infoHash, true, request, context, cancellationToken);

    public Task<TorrentPolicyDto> DeactivateTorrentAsync(string infoHash, TorrentActivationRequest request, AdminMutationContext context, CancellationToken cancellationToken)
        => SetTorrentEnabledAsync(infoHash, false, request, context, cancellationToken);

    public async Task DeleteTorrentAsync(string infoHash, long? expectedVersion, AdminMutationContext context, CancellationToken cancellationToken)
    {
        var normalizedInfoHash = infoHash.ToUpperInvariant();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var torrent = await dbContext.Torrents.Include(static item => item.Policy)
            .SingleOrDefaultAsync(item => item.InfoHash == normalizedInfoHash, cancellationToken);
        if (torrent is null)
        {
            throw new ConfigurationEntityNotFoundException("torrent", normalizedInfoHash);
        }

        var currentVersion = torrent.Policy?.RowVersion ?? 0;
        if (expectedVersion.HasValue && expectedVersion.Value != currentVersion)
        {
            throw new ConfigurationConcurrencyException("torrent", normalizedInfoHash, expectedVersion.Value, currentVersion);
        }

        if (torrent.Policy is not null)
        {
            dbContext.TorrentPolicies.Remove(torrent.Policy);
        }

        dbContext.Torrents.Remove(torrent);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await invalidationPublisher.PublishTorrentPolicyInvalidationAsync(normalizedInfoHash, cancellationToken);
        await EnqueueAuditAsync(context, "torrent.delete", "torrent", normalizedInfoHash, null, cancellationToken);
    }

    public async Task<PasskeyAccessDto> UpsertPasskeyAsync(string passkey, PasskeyUpsertRequest request, AdminMutationContext context, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Passkeys.SingleOrDefaultAsync(item => item.Passkey == passkey, cancellationToken);
        var nextVersion = 1L;

        if (entity is null)
        {
            if (request.ExpectedVersion.HasValue)
            {
                throw new ConfigurationConcurrencyException("passkey", MaskPasskey(passkey), request.ExpectedVersion.Value, 0);
            }

            entity = new PasskeyCredentialEntity
            {
                Id = Guid.NewGuid(),
                Passkey = passkey
            };
            dbContext.Passkeys.Add(entity);
        }
        else
        {
            if (request.ExpectedVersion.HasValue && request.ExpectedVersion.Value != entity.RowVersion)
            {
                throw new ConfigurationConcurrencyException("passkey", MaskPasskey(passkey), request.ExpectedVersion.Value, entity.RowVersion);
            }

            nextVersion = entity.RowVersion + 1;
        }

        entity.UserId = request.UserId;
        entity.IsRevoked = request.IsRevoked;
        entity.ExpiresAtUtc = request.ExpiresAtUtc?.UtcDateTime;
        entity.RowVersion = nextVersion;
        await dbContext.SaveChangesAsync(cancellationToken);

        await invalidationPublisher.PublishPasskeyInvalidationAsync(passkey, cancellationToken);
        await EnqueueAuditAsync(
            context,
            "passkey.upsert",
            "passkey",
            MaskPasskey(passkey),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                request.UserId,
                request.IsRevoked,
                request.ExpiresAtUtc,
                PasskeyMask = MaskPasskey(passkey)
            }, AuditJsonOptions),
            cancellationToken);

        return MapPasskey(entity, nextVersion);
    }

    public Task<PasskeyAccessDto> CreatePasskeyAsync(PasskeyCreateRequest request, AdminMutationContext context, CancellationToken cancellationToken)
        => UpsertPasskeyAsync(
            request.Passkey,
            new PasskeyUpsertRequest(request.UserId, request.IsRevoked, request.ExpiresAtUtc, request.ExpectedVersion),
            context,
            cancellationToken);

    public async Task<PasskeyAccessDto> UpsertPasskeyByIdAsync(Guid id, PasskeyUpsertRequest request, AdminMutationContext context, CancellationToken cancellationToken)
    {
        var entity = await LoadPasskeyByIdAsync(id, cancellationToken);
        if (entity is null)
        {
            throw new ConfigurationEntityNotFoundException("passkey", id.ToString("D"));
        }

        return await UpsertPasskeyAsync(entity.Passkey, request, context, cancellationToken);
    }

    public async Task<PasskeyAccessDto> RevokePasskeyAsync(string passkey, PasskeyRevokeRequest request, AdminMutationContext context, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Passkeys.SingleOrDefaultAsync(item => item.Passkey == passkey, cancellationToken);
        if (entity is null)
        {
            throw new ConfigurationEntityNotFoundException("passkey", MaskPasskey(passkey));
        }

        if (request.ExpectedVersion.HasValue && request.ExpectedVersion.Value != entity.RowVersion)
        {
            throw new ConfigurationConcurrencyException("passkey", MaskPasskey(passkey), request.ExpectedVersion.Value, entity.RowVersion);
        }

        var nextVersion = entity.RowVersion + 1;
        entity.IsRevoked = true;
        entity.RowVersion = nextVersion;
        await dbContext.SaveChangesAsync(cancellationToken);

        await invalidationPublisher.PublishPasskeyInvalidationAsync(passkey, cancellationToken);
        await EnqueueAuditAsync(
            context,
            "passkey.revoke",
            "passkey",
            MaskPasskey(passkey),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                request.ExpectedVersion,
                PasskeyMask = MaskPasskey(passkey)
            }, AuditJsonOptions),
            cancellationToken);

        return MapPasskey(entity, nextVersion);
    }

    public async Task<PasskeyAccessDto> RevokePasskeyByIdAsync(Guid id, PasskeyRevokeRequest request, AdminMutationContext context, CancellationToken cancellationToken)
    {
        var entity = await LoadPasskeyByIdAsync(id, cancellationToken);
        if (entity is null)
        {
            throw new ConfigurationEntityNotFoundException("passkey", id.ToString("D"));
        }

        return await RevokePasskeyAsync(entity.Passkey, request, context, cancellationToken);
    }

    public async Task<(PasskeyAccessDto RevokedSnapshot, PasskeyAccessDto NewSnapshot)> RotatePasskeyAsync(string passkey, PasskeyRotateRequest request, AdminMutationContext context, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var currentEntity = await dbContext.Passkeys.SingleOrDefaultAsync(item => item.Passkey == passkey, cancellationToken);
        if (currentEntity is null)
        {
            throw new ConfigurationEntityNotFoundException("passkey", MaskPasskey(passkey));
        }

        if (request.ExpectedVersion.HasValue && request.ExpectedVersion.Value != currentEntity.RowVersion)
        {
            throw new ConfigurationConcurrencyException("passkey", MaskPasskey(passkey), request.ExpectedVersion.Value, currentEntity.RowVersion);
        }

        DateTimeOffset? currentExpiresAtUtc = currentEntity.ExpiresAtUtc is null
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(currentEntity.ExpiresAtUtc.Value, DateTimeKind.Utc));
        var revokedVersion = currentEntity.RowVersion + 1;
        var newPasskey = GeneratePasskey();
        var nextExpiresAtUtc = request.ExpiresAtUtc ?? currentExpiresAtUtc;

        currentEntity.IsRevoked = true;
        currentEntity.RowVersion = revokedVersion;
        dbContext.Passkeys.Add(new PasskeyCredentialEntity
        {
            Id = Guid.NewGuid(),
            Passkey = newPasskey,
            UserId = currentEntity.UserId,
            IsRevoked = false,
            ExpiresAtUtc = nextExpiresAtUtc?.UtcDateTime,
            RowVersion = 1
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await invalidationPublisher.PublishPasskeyInvalidationAsync(passkey, cancellationToken);
        await invalidationPublisher.PublishPasskeyInvalidationAsync(newPasskey, cancellationToken);
        await EnqueueAuditAsync(
            context,
            "passkey.rotate",
            "passkey",
            $"{MaskPasskey(passkey)}->{MaskPasskey(newPasskey)}",
            System.Text.Json.JsonSerializer.Serialize(new
            {
                request.ExpiresAtUtc,
                OldPasskeyMask = MaskPasskey(passkey),
                NewPasskeyMask = MaskPasskey(newPasskey)
            }, AuditJsonOptions),
            cancellationToken);

        var newEntity = await dbContext.Passkeys.SingleAsync(item => item.Passkey == newPasskey, cancellationToken);
        return (
            MapPasskey(currentEntity, revokedVersion),
            MapPasskey(newEntity, 1));
    }

    public async Task<(PasskeyAccessDto RevokedSnapshot, PasskeyAccessDto NewSnapshot)> RotatePasskeyByIdAsync(Guid id, PasskeyRotateRequest request, AdminMutationContext context, CancellationToken cancellationToken)
    {
        var entity = await LoadPasskeyByIdAsync(id, cancellationToken);
        if (entity is null)
        {
            throw new ConfigurationEntityNotFoundException("passkey", id.ToString("D"));
        }

        return await RotatePasskeyAsync(entity.Passkey, request, context, cancellationToken);
    }

    public async Task DeletePasskeyByIdAsync(Guid id, long? expectedVersion, AdminMutationContext context, CancellationToken cancellationToken)
    {
        var entity = await LoadPasskeyByIdAsync(id, cancellationToken);
        if (entity is null)
        {
            throw new ConfigurationEntityNotFoundException("passkey", id.ToString("D"));
        }

        if (expectedVersion.HasValue && expectedVersion.Value != entity.RowVersion)
        {
            throw new ConfigurationConcurrencyException("passkey", MaskPasskey(entity.Passkey), expectedVersion.Value, entity.RowVersion);
        }

        dbContext.Passkeys.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        await invalidationPublisher.PublishPasskeyInvalidationAsync(entity.Passkey, cancellationToken);
        await EnqueueAuditAsync(context, "passkey.delete", "passkey", MaskPasskey(entity.Passkey), null, cancellationToken);
    }

    public async Task<TrackerAccessRightsDto> UpsertTrackerAccessRightsAsync(Guid userId, TrackerAccessRightsUpsertRequest request, AdminMutationContext context, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Permissions.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        var nextVersion = 1L;

        if (entity is null)
        {
            if (request.ExpectedVersion.HasValue)
            {
                throw new ConfigurationConcurrencyException("tracker_access", userId.ToString("D"), request.ExpectedVersion.Value, 0);
            }

            entity = new UserPermissionEntity
            {
                UserId = userId
            };
            dbContext.Permissions.Add(entity);
        }
        else
        {
            if (request.ExpectedVersion.HasValue && request.ExpectedVersion.Value != entity.RowVersion)
            {
                throw new ConfigurationConcurrencyException("tracker_access", userId.ToString("D"), request.ExpectedVersion.Value, entity.RowVersion);
            }

            nextVersion = entity.RowVersion + 1;
        }

        entity.CanLeech = request.CanLeech;
        entity.CanSeed = request.CanSeed;
        entity.CanScrape = request.CanScrape;
        entity.CanUsePrivateTracker = request.CanUsePrivateTracker;
        entity.RowVersion = nextVersion;
        await dbContext.SaveChangesAsync(cancellationToken);

        await invalidationPublisher.PublishUserPermissionInvalidationAsync(userId, cancellationToken);
        await EnqueueAuditAsync(
            context,
            "tracker_access.upsert",
            "user",
            userId.ToString("D"),
            System.Text.Json.JsonSerializer.Serialize(request, AuditJsonOptions),
            cancellationToken);

        return new TrackerAccessRightsDto(userId, request.CanLeech, request.CanSeed, request.CanScrape, request.CanUsePrivateTracker, nextVersion);
    }

    public async Task DeleteTrackerAccessRightsAsync(Guid userId, long? expectedVersion, AdminMutationContext context, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Permissions.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (entity is null)
        {
            throw new ConfigurationEntityNotFoundException("tracker_access", userId.ToString("D"));
        }

        if (expectedVersion.HasValue && expectedVersion.Value != entity.RowVersion)
        {
            throw new ConfigurationConcurrencyException("tracker_access", userId.ToString("D"), expectedVersion.Value, entity.RowVersion);
        }

        dbContext.Permissions.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        await invalidationPublisher.PublishUserPermissionInvalidationAsync(userId, cancellationToken);
        await EnqueueAuditAsync(context, "tracker_access.delete", "user", userId.ToString("D"), null, cancellationToken);
    }

    public async Task<TrackerNodeConfigurationDto> UpsertTrackerNodeConfigurationAsync(string nodeKey, TrackerNodeConfigurationUpsertRequest request, AdminMutationContext context, CancellationToken cancellationToken)
    {
        var entity = await dbContext.TrackerNodeConfigurations.SingleOrDefaultAsync(item => item.NodeKey == nodeKey, cancellationToken);
        var effectiveConfiguration = request.Configuration;
        if (entity is not null)
        {
            var currentConfiguration = TrackerNodeConfigurationSerialization.Deserialize(entity.ConfigJson);
            effectiveConfiguration = effectiveConfiguration with
            {
                Redis = effectiveConfiguration.Redis with
                {
                    Configuration = string.IsNullOrWhiteSpace(effectiveConfiguration.Redis.Configuration)
                        ? currentConfiguration.Redis.Configuration
                        : effectiveConfiguration.Redis.Configuration
                },
                Postgres = effectiveConfiguration.Postgres with
                {
                    ConnectionString = string.IsNullOrWhiteSpace(effectiveConfiguration.Postgres.ConnectionString)
                        ? currentConfiguration.Postgres.ConnectionString
                        : effectiveConfiguration.Postgres.ConnectionString
                }
            };
        }

        var validation = TrackerNodeConfigurationValidator.Validate(effectiveConfiguration);
        var errors = validation.Issues.Where(static issue => issue.Severity == TrackerConfigValidationSeverity.Error).ToArray();
        if (errors.Length > 0)
        {
            throw new ValidationException($"Tracker node configuration is invalid: {string.Join("; ", errors.Select(static issue => issue.Message))}");
        }

        var nextVersion = 1L;

        if (entity is null)
        {
            if (request.ExpectedVersion.HasValue)
            {
                throw new ConfigurationConcurrencyException("tracker_node_configuration", nodeKey, request.ExpectedVersion.Value, 0);
            }

            entity = new TrackerNodeConfigurationEntity
            {
                NodeKey = nodeKey
            };
            dbContext.TrackerNodeConfigurations.Add(entity);
        }
        else
        {
            if (request.ExpectedVersion.HasValue && request.ExpectedVersion.Value != entity.RowVersion)
            {
                throw new ConfigurationConcurrencyException("tracker_node_configuration", nodeKey, request.ExpectedVersion.Value, entity.RowVersion);
            }

            nextVersion = entity.RowVersion + 1;
        }

        entity.ConfigJson = TrackerNodeConfigurationSerialization.Serialize(effectiveConfiguration);
        entity.RowVersion = nextVersion;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedBy = context.ActorId;
        entity.ApplyMode = request.ApplyMode.ToString();
        entity.RequiresRestart = request.ApplyMode != TrackerNodeConfigurationApplyMode.Dynamic;
        await dbContext.SaveChangesAsync(cancellationToken);

        await EnqueueAuditAsync(
            context,
            "tracker_node_configuration.upsert",
            "tracker_node_configuration",
            nodeKey,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                request.ApplyMode,
                effectiveConfiguration.Identity.NodeId,
                effectiveConfiguration.Identity.NodeName,
                effectiveConfiguration.Identity.Region,
                effectiveConfiguration.Http.EnableAnnounce,
                effectiveConfiguration.Http.EnableScrape,
                effectiveConfiguration.Udp.Enabled,
                effectiveConfiguration.Policy.EnablePublicTracker,
                effectiveConfiguration.Policy.EnablePrivateTracker
            }, AuditJsonOptions),
            cancellationToken);

        return new TrackerNodeConfigurationDto(
            nodeKey,
            effectiveConfiguration,
            nextVersion,
            new DateTimeOffset(DateTime.SpecifyKind(entity.UpdatedAtUtc, DateTimeKind.Utc)),
            entity.UpdatedBy,
            request.ApplyMode,
            entity.RequiresRestart);
    }

    public async Task<BanRuleDto> UpsertBanRuleAsync(string scope, string subject, BanRuleUpsertRequest request, AdminMutationContext context, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Bans.SingleOrDefaultAsync(item => item.Scope == scope && item.Subject == subject, cancellationToken);
        var nextVersion = 1L;

        if (entity is null)
        {
            if (request.ExpectedVersion.HasValue)
            {
                throw new ConfigurationConcurrencyException("ban", $"{scope}:{subject}", request.ExpectedVersion.Value, 0);
            }

            entity = new BanRuleEntity
            {
                Scope = scope,
                Subject = subject
            };
            dbContext.Bans.Add(entity);
        }
        else
        {
            if (request.ExpectedVersion.HasValue && request.ExpectedVersion.Value != entity.RowVersion)
            {
                throw new ConfigurationConcurrencyException("ban", $"{scope}:{subject}", request.ExpectedVersion.Value, entity.RowVersion);
            }

            nextVersion = entity.RowVersion + 1;
        }

        entity.Reason = request.Reason;
        entity.ExpiresAtUtc = request.ExpiresAtUtc?.UtcDateTime;
        entity.RowVersion = nextVersion;
        await dbContext.SaveChangesAsync(cancellationToken);

        await invalidationPublisher.PublishBanRuleInvalidationAsync(scope, subject, cancellationToken);
        await EnqueueAuditAsync(
            context,
            "ban.upsert",
            "ban",
            $"{scope}:{subject}",
            System.Text.Json.JsonSerializer.Serialize(request, AuditJsonOptions),
            cancellationToken);

        return new BanRuleDto(scope, subject, request.Reason, request.ExpiresAtUtc, nextVersion);
    }

    public async Task<BanRuleDto> ExpireBanRuleAsync(string scope, string subject, BanRuleExpireRequest request, AdminMutationContext context, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Bans.SingleOrDefaultAsync(item => item.Scope == scope && item.Subject == subject, cancellationToken);
        if (entity is null)
        {
            throw new ConfigurationEntityNotFoundException("ban", $"{scope}:{subject}");
        }

        if (request.ExpectedVersion.HasValue && request.ExpectedVersion.Value != entity.RowVersion)
        {
            throw new ConfigurationConcurrencyException("ban", $"{scope}:{subject}", request.ExpectedVersion.Value, entity.RowVersion);
        }

        var nextVersion = entity.RowVersion + 1;
        entity.ExpiresAtUtc = request.ExpiresAtUtc.UtcDateTime;
        entity.RowVersion = nextVersion;
        await dbContext.SaveChangesAsync(cancellationToken);

        await invalidationPublisher.PublishBanRuleInvalidationAsync(scope, subject, cancellationToken);
        await EnqueueAuditAsync(
            context,
            "ban.expire",
            "ban",
            $"{scope}:{subject}",
            System.Text.Json.JsonSerializer.Serialize(request, AuditJsonOptions),
            cancellationToken);

        return new BanRuleDto(
            scope,
            subject,
            entity.Reason,
            request.ExpiresAtUtc,
            nextVersion);
    }

    public async Task DeleteBanRuleAsync(string scope, string subject, long? expectedVersion, AdminMutationContext context, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Bans.SingleOrDefaultAsync(item => item.Scope == scope && item.Subject == subject, cancellationToken);
        if (entity is null)
        {
            throw new ConfigurationEntityNotFoundException("ban", $"{scope}:{subject}");
        }

        if (expectedVersion.HasValue && expectedVersion.Value != entity.RowVersion)
        {
            throw new ConfigurationConcurrencyException("ban", $"{scope}:{subject}", expectedVersion.Value, entity.RowVersion);
        }

        dbContext.Bans.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        await invalidationPublisher.PublishBanRuleInvalidationAsync(scope, subject, cancellationToken);
        await EnqueueAuditAsync(
            context,
            "ban.delete",
            "ban",
            $"{scope}:{subject}",
            null,
            cancellationToken);
    }

    private async Task<TorrentConfigurationEntity> EnsureTorrentAsync(
        TorrentPolicyCurrentState currentState,
        string normalizedInfoHash,
        bool isPrivate,
        bool isEnabled,
        CancellationToken cancellationToken)
    {
        var torrent = await dbContext.Torrents.SingleOrDefaultAsync(torrent => torrent.InfoHash == normalizedInfoHash, cancellationToken);
        if (torrent is null)
        {
            torrent = new TorrentConfigurationEntity
            {
                Id = currentState.TorrentId,
                InfoHash = normalizedInfoHash
            };
            dbContext.Torrents.Add(torrent);
        }

        torrent.IsPrivate = isPrivate;
        torrent.IsEnabled = isEnabled;
        return torrent;
    }

    private async Task<TorrentPolicyCurrentState> LoadTorrentPolicyCurrentStateAsync(string normalizedInfoHash, CancellationToken cancellationToken)
    {
        var projection = await dbContext.Torrents
            .AsNoTracking()
            .Where(torrent => torrent.InfoHash == normalizedInfoHash)
            .Select(static torrent => new
            {
                torrent.Id,
                torrent.IsPrivate,
                torrent.IsEnabled,
                Policy = torrent.Policy
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (projection is null)
        {
            return new TorrentPolicyCurrentState(false, null, Guid.NewGuid(), 0);
        }

        var currentSnapshot = projection.Policy is null
            ? null
            : new TorrentPolicyDto(
                normalizedInfoHash,
                projection.IsPrivate,
                projection.IsEnabled,
                projection.Policy.AnnounceIntervalSeconds,
                projection.Policy.MinAnnounceIntervalSeconds,
                projection.Policy.DefaultNumWant,
                projection.Policy.MaxNumWant,
                projection.Policy.AllowScrape,
                projection.Policy.RowVersion,
                projection.Policy.WarningMessage);

        return new TorrentPolicyCurrentState(
            true,
            currentSnapshot,
            projection.Id,
            projection.Policy?.RowVersion ?? 0);
    }

    private static TorrentPolicyDto BuildProposedTorrentPolicy(string normalizedInfoHash, TorrentPolicyUpsertRequest request, long version)
        => new(
            normalizedInfoHash,
            request.IsPrivate,
            request.IsEnabled,
            request.AnnounceIntervalSeconds,
            request.MinAnnounceIntervalSeconds,
            request.DefaultNumWant,
            request.MaxNumWant,
            request.AllowScrape,
            version,
            request.WarningMessage,
            request.CompactOnly,
            request.AllowUdp,
            request.AllowIPv6,
            request.StrictnessProfileOverride,
            request.CompatibilityModeOverride,
            request.ModerationState,
            request.MaintenanceFlag,
            request.TemporaryRestriction);

    private async Task<TorrentPolicyDto> SetTorrentEnabledAsync(string infoHash, bool isEnabled, TorrentActivationRequest request, AdminMutationContext context, CancellationToken cancellationToken)
    {
        var normalizedInfoHash = infoHash.ToUpperInvariant();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var torrent = await dbContext.Torrents.Include(static item => item.Policy)
            .SingleOrDefaultAsync(item => item.InfoHash == normalizedInfoHash, cancellationToken);
        if (torrent is null)
        {
            throw new ConfigurationEntityNotFoundException("torrent", normalizedInfoHash);
        }

        var currentVersion = torrent.Policy?.RowVersion ?? 0;
        if (request.ExpectedVersion.HasValue && request.ExpectedVersion.Value != currentVersion)
        {
            throw new ConfigurationConcurrencyException("torrent", normalizedInfoHash, request.ExpectedVersion.Value, currentVersion);
        }

        var nextVersion = currentVersion + 1;
        torrent.IsEnabled = isEnabled;

        if (torrent.Policy is null)
        {
            torrent.Policy = new TorrentPolicyEntity
            {
                TorrentId = torrent.Id,
                AnnounceIntervalSeconds = 1800,
                MinAnnounceIntervalSeconds = 900,
                DefaultNumWant = 50,
                MaxNumWant = 100,
                AllowScrape = true,
                RowVersion = nextVersion
            };
            dbContext.TorrentPolicies.Add(torrent.Policy);
        }
        else
        {
            torrent.Policy.RowVersion = nextVersion;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var action = isEnabled ? "torrent.activate" : "torrent.deactivate";
        await invalidationPublisher.PublishTorrentPolicyInvalidationAsync(normalizedInfoHash, cancellationToken);
        await EnqueueAuditAsync(
            context,
            action,
            "torrent",
            normalizedInfoHash,
            System.Text.Json.JsonSerializer.Serialize(new { IsEnabled = isEnabled, request.ExpectedVersion }, AuditJsonOptions),
            cancellationToken);

        return new TorrentPolicyDto(
            normalizedInfoHash,
            torrent.IsPrivate,
            torrent.IsEnabled,
            torrent.Policy.AnnounceIntervalSeconds,
            torrent.Policy.MinAnnounceIntervalSeconds,
            torrent.Policy.DefaultNumWant,
            torrent.Policy.MaxNumWant,
            torrent.Policy.AllowScrape,
            nextVersion,
            torrent.Policy.WarningMessage);
    }

    private async Task EnqueueAuditAsync(
        AdminMutationContext context,
        string action,
        string entityType,
        string entityId,
        string? afterJson,
        CancellationToken cancellationToken)
    {
        await auditBuffer.EnqueueAsync(
            new AuditWriteRequest(
                DateTimeOffset.UtcNow,
                context.ActorId,
                context.ActorRole,
                action,
                AuditSeverityResolver.Resolve(action),
                entityType,
                entityId,
                context.CorrelationId,
                context.RequestId,
                "success",
                context.IpAddress,
                context.UserAgent,
                null,
                afterJson),
            cancellationToken);
    }

    private static IReadOnlyList<string> BuildTorrentPolicyPreviewWarnings(TorrentPolicyCurrentState currentState)
    {
        var warnings = new List<string>(2);
        if (currentState.TorrentExists && currentState.CurrentSnapshot is null)
        {
            warnings.Add("This change will create a new torrent policy row for an existing torrent that currently has no explicit policy row.");
        }

        if (!currentState.TorrentExists)
        {
            warnings.Add("This change will create a new torrent and its initial policy row.");
        }

        return warnings;
    }

    private static void ValidateTorrentPolicyExpectedVersion(string normalizedInfoHash, long? expectedVersion, long currentVersion)
    {
        if (expectedVersion.HasValue && expectedVersion.Value != currentVersion)
        {
            throw new ConfigurationConcurrencyException("torrent_policy", normalizedInfoHash, expectedVersion.Value, currentVersion);
        }
    }

    private async Task<PasskeyCredentialEntity?> LoadPasskeyByIdAsync(Guid id, CancellationToken cancellationToken)
        => await dbContext.Passkeys.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    private static PasskeyAccessDto MapPasskey(PasskeyCredentialEntity entity, long version)
        => new(
            entity.Id,
            entity.Passkey,
            entity.UserId,
            entity.IsRevoked,
            entity.ExpiresAtUtc is null ? null : new DateTimeOffset(DateTime.SpecifyKind(entity.ExpiresAtUtc.Value, DateTimeKind.Utc)),
            version);
}

internal sealed class EfConfigurationMaintenanceService(
    TrackerConfigurationDbContext dbContext,
    BeeTracker.Caching.Redis.IRedisCacheClient redisCacheClient,
    IAuditBuffer auditBuffer) : IConfigurationMaintenanceService
{
    private const string RefreshChannelName = "tracker:config-refresh";

    public async Task TriggerCacheRefreshAsync(string operation, AdminMutationContext context, CancellationToken cancellationToken)
    {
        dbContext.MaintenanceRuns.Add(new MaintenanceRunEntity
        {
            Id = Guid.NewGuid(),
            Operation = operation,
            RequestedBy = context.ActorId,
            RequestedAtUtc = DateTime.UtcNow,
            Status = "requested",
            CorrelationId = context.CorrelationId
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        await redisCacheClient.Subscriber.PublishAsync(StackExchange.Redis.RedisChannel.Literal(RefreshChannelName), operation);
        await auditBuffer.EnqueueAsync(
            new AuditWriteRequest(
                DateTimeOffset.UtcNow,
                context.ActorId,
                context.ActorRole,
                "maintenance.trigger",
                AuditSeverityResolver.Resolve("maintenance.trigger"),
                "maintenance",
                operation,
                context.CorrelationId,
                context.RequestId,
                "success",
                context.IpAddress,
                context.UserAgent,
                null,
                null),
            cancellationToken);
    }
}

internal sealed class EfAuditWriterBackgroundService(
    IAuditBuffer auditBuffer,
    IServiceScopeFactory serviceScopeFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var batch in auditBuffer.ReadBatchesAsync(stoppingToken))
        {
            using var scope = serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TrackerConfigurationDbContext>();
            dbContext.AuditRecords.AddRange(batch.Select(static auditRecord => new AuditRecordEntity
            {
                Id = Guid.NewGuid(),
                OccurredAtUtc = auditRecord.OccurredAtUtc.UtcDateTime,
                ActorId = auditRecord.ActorId,
                ActorRole = auditRecord.ActorRole,
                Action = auditRecord.Action,
                Severity = auditRecord.Severity,
                EntityType = auditRecord.EntityType,
                EntityId = auditRecord.EntityId,
                CorrelationId = auditRecord.CorrelationId,
                RequestId = auditRecord.RequestId,
                Result = auditRecord.Result,
                IpAddress = auditRecord.IpAddress,
                UserAgent = auditRecord.UserAgent,
                BeforeJson = auditRecord.BeforeJson,
                AfterJson = auditRecord.AfterJson
            }));
            await dbContext.SaveChangesAsync(stoppingToken);
        }
    }
}
