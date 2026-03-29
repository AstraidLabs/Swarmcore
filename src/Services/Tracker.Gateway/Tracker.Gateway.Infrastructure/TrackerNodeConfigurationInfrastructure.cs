using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using BeeTracker.BuildingBlocks.Abstractions.Options;
using BeeTracker.Contracts.Configuration;
using Tracker.ConfigurationService.Application;
using Tracker.Gateway.Application.Announce;
using Tracker.Gateway.Runtime;
using Tracker.UdpTracker.Service;

namespace Tracker.Gateway.Infrastructure;

public interface ITrackerNodeConfigurationSnapshotAccessor
{
    TrackerNodeConfigurationDto Current { get; }
    TrackerNodeConfigurationValidationResultDto Validation { get; }
}

public sealed class TrackerNodeConfigurationSnapshotAccessor : ITrackerNodeConfigurationSnapshotAccessor
{
    private TrackerNodeConfigurationDto? _current;
    private TrackerNodeConfigurationValidationResultDto? _validation;

    public TrackerNodeConfigurationDto Current
        => _current ?? throw new InvalidOperationException("Tracker node configuration snapshot has not been initialized.");

    public TrackerNodeConfigurationValidationResultDto Validation
        => _validation ?? throw new InvalidOperationException("Tracker node configuration validation has not been initialized.");

    internal void Set(TrackerNodeConfigurationDto current, TrackerNodeConfigurationValidationResultDto validation)
    {
        _current = current;
        _validation = validation;
    }
}

public sealed class TrackerNodeConfigurationBootstrapper(
    IOptions<TrackerNodeOptions> nodeOptionsAccessor,
    IOptions<TrackerSecurityOptions> securityOptionsAccessor,
    IOptions<TrackerCompatibilityOptions> compatibilityOptionsAccessor,
    IOptions<TrackerGovernanceOptions> governanceOptionsAccessor,
    IOptions<TrackerAbuseProtectionOptions> abuseOptionsAccessor,
    IOptions<GatewayRuntimeOptions> runtimeOptionsAccessor,
    IOptions<UdpTrackerOptions> udpOptionsAccessor,
    IOptions<RedisOptions> redisOptionsAccessor,
    IOptions<PostgresOptions> postgresOptionsAccessor,
    IOptions<ClusterShardingOptions> shardingOptionsAccessor,
    IOptions<TelemetryBatchingOptions> telemetryOptionsAccessor,
    IOptions<DependencyDegradationOptions> degradationOptionsAccessor,
    ITrackerNodeConfigurationReader trackerNodeConfigurationReader,
    TrackerNodeConfigurationSnapshotAccessor snapshotAccessor,
    ILogger<TrackerNodeConfigurationBootstrapper> logger)
{
    public async Task<TrackerNodeConfigurationDto> InitializeAsync(CancellationToken cancellationToken)
    {
        var bootstrap = BuildBootstrapDocument(
            nodeOptionsAccessor.Value,
            securityOptionsAccessor.Value,
            compatibilityOptionsAccessor.Value,
            governanceOptionsAccessor.Value,
            abuseOptionsAccessor.Value,
            runtimeOptionsAccessor.Value,
            udpOptionsAccessor.Value,
            redisOptionsAccessor.Value,
            postgresOptionsAccessor.Value,
            shardingOptionsAccessor.Value,
            telemetryOptionsAccessor.Value,
            degradationOptionsAccessor.Value);

        var nodeKey = bootstrap.Identity.NodeId;
        TrackerNodeConfigurationDto? persisted = null;
        try
        {
            persisted = await trackerNodeConfigurationReader.GetTrackerNodeConfigurationAsync(nodeKey, cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            logger.LogWarning(exception, "Tracker node configuration table is not available yet. Falling back to bootstrap configuration for node {NodeKey}.", nodeKey);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Persisted tracker node configuration could not be loaded for node {NodeKey}. Falling back to bootstrap configuration.", nodeKey);
        }

        var effective = persisted ?? new TrackerNodeConfigurationDto(
            nodeKey,
            bootstrap,
            0,
            DateTimeOffset.UtcNow,
            "bootstrap",
            TrackerNodeConfigurationApplyMode.StartupOnly,
            false);

        var validation = await trackerNodeConfigurationReader.ValidateTrackerNodeConfigurationAsync(effective.Configuration, cancellationToken);
        foreach (var issue in validation.Issues)
        {
            if (issue.Severity == TrackerConfigValidationSeverity.Error)
            {
                logger.LogError("Tracker node config validation error [{Code}] {Path}: {Message}", issue.Code, issue.Path, issue.Message);
            }
            else
            {
                logger.LogWarning("Tracker node config validation warning [{Code}] {Path}: {Message}", issue.Code, issue.Path, issue.Message);
            }
        }

        if (!validation.IsValid)
        {
            throw new InvalidOperationException("Tracker node configuration validation failed. See logged errors above.");
        }

        snapshotAccessor.Set(effective, validation);
        return effective;
    }

    internal static TrackerNodeConfigurationDocument BuildBootstrapDocument(
        TrackerNodeOptions node,
        TrackerSecurityOptions security,
        TrackerCompatibilityOptions compatibility,
        TrackerGovernanceOptions governance,
        TrackerAbuseProtectionOptions abuse,
        GatewayRuntimeOptions runtime,
        UdpTrackerOptions udp,
        RedisOptions redis,
        PostgresOptions postgres,
        ClusterShardingOptions sharding,
        TelemetryBatchingOptions telemetry,
        DependencyDegradationOptions degradation)
    {
        return new TrackerNodeConfigurationDocument(
            new TrackerNodeIdentityConfig(
                node.NodeId,
                node.NodeName,
                node.Environment,
                node.Region,
                node.PublicBaseUrl,
                node.InternalBaseUrl,
                SupportsHttp: true,
                SupportsUdp: udp.Enabled,
                SupportsPrivateTracker: true,
                SupportsPublicTracker: true),
            new HttpTrackerConfig(
                EnableAnnounce: !governance.AnnounceDisabled,
                EnableScrape: !governance.ScrapeDisabled,
                AnnounceRoute: NormalizeRoute(node.AnnounceRoute),
                PrivateAnnounceRoute: NormalizeRoute(node.PrivateAnnounceRoute),
                ScrapeRoute: NormalizeRoute(node.ScrapeRoute),
                DefaultAnnounceIntervalSeconds: node.DefaultAnnounceIntervalSeconds,
                MinAnnounceIntervalSeconds: node.MinAnnounceIntervalSeconds,
                DefaultNumWant: node.DefaultNumWant,
                MaxNumWant: security.HardMaxNumWant,
                CompactResponsesByDefault: security.RequireCompactResponses,
                AllowNonCompactResponses: !security.RequireCompactResponses,
                AllowPasskeyInPath: true,
                AllowPasskeyInQuery: security.AllowPasskeyInQueryString,
                AllowClientIpOverride: security.AllowClientIpOverride,
                EmitWarningMessages: true),
            new UdpTrackerConfig(
                udp.Enabled,
                udp.BindAddress,
                udp.Port,
                udp.ConnectionTimeoutSeconds,
                udp.ReceiveBufferSize,
                udp.MaxDatagramSize,
                udp.EnableScrape,
                Math.Min(udp.MaxScrapeInfoHashes, security.MaxScrapeInfoHashes)),
            new RuntimeStoreConfig(
                runtime.ShardCount,
                runtime.PeerTtlSeconds,
                runtime.ExpirySweepIntervalSeconds,
                runtime.MaxPeersPerResponse,
                runtime.MaxPeersPerSwarm,
                runtime.PreferLocalShardPeers,
                runtime.EnableCompletedAccounting,
                runtime.EnableIPv6Peers || security.AllowIPv6Peers),
            new TrackerPolicyConfig(
                EnablePublicTracker: true,
                EnablePrivateTracker: true,
                RequirePasskeyForPrivateTracker: true,
                AllowPublicScrape: true,
                AllowPrivateScrape: true,
                DefaultTorrentVisibility: "private",
                StrictnessProfile: compatibility.StrictnessProfile.ToString(),
                CompatibilityMode: compatibility.CompatibilityMode.ToString()),
            new RedisCoordinationConfig(
                Enabled: !string.IsNullOrWhiteSpace(redis.Configuration),
                redis.Configuration,
                redis.KeyPrefix,
                redis.PolicyCacheTtlSeconds,
                redis.SnapshotCacheTtlSeconds,
                redis.InvalidationChannel,
                redis.HeartbeatTtlSeconds,
                redis.OwnershipLeaseDurationSeconds,
                redis.OwnershipRefreshIntervalSeconds,
                redis.SwarmSummaryPublishIntervalSeconds,
                redis.SwarmSummaryTtlSeconds),
            new PostgresPersistenceConfig(
                Enabled: !string.IsNullOrWhiteSpace(postgres.ConnectionString),
                postgres.ConnectionString,
                postgres.MigrateOnStart,
                postgres.PersistTelemetry,
                postgres.PersistAudit,
                telemetry.BatchSize,
                telemetry.FlushIntervalMilliseconds),
            new AbuseProtectionConfig(
                security.AnnounceMaxQueryLength,
                security.ScrapeMaxQueryLength,
                security.MaxQueryParameterCount,
                security.HardMaxNumWant,
                abuse.EnableAnnouncePasskeyRateLimit,
                abuse.AnnouncePerPasskeyPerSecond,
                abuse.EnableAnnounceIpRateLimit,
                abuse.AnnouncePerIpPerSecond,
                abuse.EnableScrapeIpRateLimit,
                abuse.ScrapePerIpPerSecond,
                abuse.RejectOversizedRequests,
                security.MaxScrapeInfoHashes),
            new ObservabilityConfig(
                node.EnableHealthEndpoints,
                node.EnableMetrics,
                node.EnableTracing,
                node.EnableDiagnosticsEndpoints,
                NormalizeRoute(node.LiveHealthRoute),
                NormalizeRoute(node.ReadyHealthRoute),
                NormalizeRoute(node.StartupHealthRoute)));
    }

    private static string NormalizeRoute(string route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return "/";
        }

        return route.StartsWith("/", StringComparison.Ordinal) ? route : $"/{route}";
    }
}

public static class TrackerNodeConfigurationFormatting
{
    public static string TrimRouteTemplate(string route)
    {
        var trimmed = route.Trim();
        var parameterIndex = trimmed.IndexOf("/{", StringComparison.Ordinal);
        if (parameterIndex >= 0)
        {
            return trimmed[..parameterIndex];
        }

        return trimmed.Replace("{passkey?}", string.Empty, StringComparison.Ordinal)
            .Replace("{passkey}", string.Empty, StringComparison.Ordinal)
            .TrimEnd('/');
    }
}
