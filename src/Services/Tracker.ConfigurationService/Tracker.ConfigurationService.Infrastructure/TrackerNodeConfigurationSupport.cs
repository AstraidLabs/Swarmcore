using System.Text.Json;
using BeeTracker.Contracts.Configuration;

namespace Tracker.ConfigurationService.Infrastructure;

internal static class TrackerNodeConfigurationSerialization
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static TrackerNodeConfigurationDocument Deserialize(string json)
        => JsonSerializer.Deserialize<TrackerNodeConfigurationDocument>(json, JsonOptions)
           ?? throw new InvalidOperationException("Tracker node configuration payload could not be deserialized.");

    public static string Serialize(TrackerNodeConfigurationDocument configuration)
        => JsonSerializer.Serialize(configuration, JsonOptions);
}

internal static class TrackerNodeConfigurationValidator
{
    public static TrackerNodeConfigurationValidationResultDto Validate(TrackerNodeConfigurationDocument configuration)
    {
        var issues = new List<TrackerConfigValidationIssueDto>();

        Require(issues, "identity.nodeId", configuration.Identity.NodeId, "node_id_required", "NodeId is required.");
        Require(issues, "identity.nodeName", configuration.Identity.NodeName, "node_name_required", "NodeName is required.");
        Require(issues, "identity.environment", configuration.Identity.Environment, "environment_required", "Environment is required.");
        Require(issues, "identity.region", configuration.Identity.Region, "region_required", "Region is required.");
        RequireAbsoluteUri(issues, "identity.publicBaseUrl", configuration.Identity.PublicBaseUrl, "public_base_url_invalid");
        RequireAbsoluteUri(issues, "identity.internalBaseUrl", configuration.Identity.InternalBaseUrl, "internal_base_url_invalid");

        RequirePositive(issues, "http.defaultAnnounceIntervalSeconds", configuration.Http.DefaultAnnounceIntervalSeconds, "announce_interval_invalid");
        RequirePositive(issues, "http.minAnnounceIntervalSeconds", configuration.Http.MinAnnounceIntervalSeconds, "min_announce_interval_invalid");
        RequirePositive(issues, "http.defaultNumWant", configuration.Http.DefaultNumWant, "default_numwant_invalid");
        RequirePositive(issues, "http.maxNumWant", configuration.Http.MaxNumWant, "max_numwant_invalid");
        Require(issues, "http.announceRoute", configuration.Http.AnnounceRoute, "announce_route_required", "Announce route is required.");
        Require(issues, "http.privateAnnounceRoute", configuration.Http.PrivateAnnounceRoute, "private_announce_route_required", "Private announce route is required.");
        Require(issues, "http.scrapeRoute", configuration.Http.ScrapeRoute, "scrape_route_required", "Scrape route is required.");

        if (configuration.Http.MinAnnounceIntervalSeconds > configuration.Http.DefaultAnnounceIntervalSeconds)
        {
            issues.Add(Error("announce_interval_conflict", "http.minAnnounceIntervalSeconds", "Minimum announce interval must be less than or equal to the default announce interval."));
        }

        if (configuration.Http.MaxNumWant < configuration.Http.DefaultNumWant)
        {
            issues.Add(Error("numwant_conflict", "http.maxNumWant", "Max numwant must be greater than or equal to the default numwant."));
        }

        if (!configuration.Http.AllowNonCompactResponses && !configuration.Http.CompactResponsesByDefault)
        {
            issues.Add(Error("compact_policy_conflict", "http.allowNonCompactResponses", "Non-compact responses cannot be disabled while compact default is false."));
        }

        if (!configuration.Http.AllowPasskeyInPath && !configuration.Http.AllowPasskeyInQuery && configuration.Policy.RequirePasskeyForPrivateTracker)
        {
            issues.Add(Error("passkey_routing_missing", "policy.requirePasskeyForPrivateTracker", "Private tracker mode requires at least one passkey routing strategy."));
        }

        if (configuration.Udp.Enabled)
        {
            Require(issues, "udp.bindAddress", configuration.Udp.BindAddress, "udp_bind_required", "UDP bind address is required when UDP is enabled.");
            RequirePort(issues, "udp.port", configuration.Udp.Port, "udp_port_invalid");
            RequirePositive(issues, "udp.connectionTimeoutSeconds", configuration.Udp.ConnectionTimeoutSeconds, "udp_timeout_invalid");
            RequirePositive(issues, "udp.receiveBufferSize", configuration.Udp.ReceiveBufferSize, "udp_buffer_invalid");
            RequirePositive(issues, "udp.maxDatagramSize", configuration.Udp.MaxDatagramSize, "udp_datagram_invalid");
            RequirePositive(issues, "udp.maxScrapeInfoHashes", configuration.Udp.MaxScrapeInfoHashes, "udp_scrape_limit_invalid");
        }

        RequirePositive(issues, "runtime.shardCount", configuration.Runtime.ShardCount, "runtime_shard_count_invalid");
        RequirePositive(issues, "runtime.peerTtlSeconds", configuration.Runtime.PeerTtlSeconds, "runtime_peer_ttl_invalid");
        RequirePositive(issues, "runtime.cleanupIntervalSeconds", configuration.Runtime.CleanupIntervalSeconds, "runtime_cleanup_interval_invalid");
        RequirePositive(issues, "runtime.maxPeersPerResponse", configuration.Runtime.MaxPeersPerResponse, "runtime_max_peers_response_invalid");
        if (configuration.Runtime.MaxPeersPerSwarm is { } maxPeersPerSwarm && maxPeersPerSwarm < configuration.Runtime.MaxPeersPerResponse)
        {
            issues.Add(Error("runtime_max_peers_swarm_conflict", "runtime.maxPeersPerSwarm", "Max peers per swarm must be greater than or equal to max peers per response."));
        }

        if (!configuration.Policy.EnablePublicTracker && !configuration.Policy.EnablePrivateTracker)
        {
            issues.Add(Error("policy_tracker_surface_disabled", "policy", "At least one of public or private tracker mode must be enabled."));
        }

        if (configuration.Policy.RequirePasskeyForPrivateTracker && !configuration.Policy.EnablePrivateTracker)
        {
            issues.Add(Error("policy_private_passkey_conflict", "policy.requirePasskeyForPrivateTracker", "Passkey requirement cannot be enabled while private tracker mode is disabled."));
        }

        if (!configuration.Policy.EnablePrivateTracker && configuration.Policy.AllowPrivateScrape)
        {
            issues.Add(Error("policy_private_scrape_conflict", "policy.allowPrivateScrape", "Private scrape cannot be enabled while private tracker mode is disabled."));
        }

        if (!configuration.Policy.EnablePublicTracker && configuration.Policy.AllowPublicScrape)
        {
            issues.Add(Error("policy_public_scrape_conflict", "policy.allowPublicScrape", "Public scrape cannot be enabled while public tracker mode is disabled."));
        }

        if (configuration.Redis.Enabled)
        {
            Require(issues, "redis.configuration", configuration.Redis.Configuration, "redis_configuration_required", "Redis connection string is required when Redis coordination is enabled.");
            Require(issues, "redis.keyPrefix", configuration.Redis.KeyPrefix, "redis_key_prefix_required", "Redis key prefix is required.");
            Require(issues, "redis.invalidationChannel", configuration.Redis.InvalidationChannel, "redis_invalidation_channel_required", "Redis invalidation channel is required.");
            RequirePositive(issues, "redis.heartbeatTtlSeconds", configuration.Redis.HeartbeatTtlSeconds, "redis_heartbeat_ttl_invalid");
            RequirePositive(issues, "redis.ownershipLeaseDurationSeconds", configuration.Redis.OwnershipLeaseDurationSeconds, "redis_lease_duration_invalid");
            RequirePositive(issues, "redis.ownershipRefreshIntervalSeconds", configuration.Redis.OwnershipRefreshIntervalSeconds, "redis_lease_refresh_invalid");
            RequirePositive(issues, "redis.swarmSummaryPublishIntervalSeconds", configuration.Redis.SwarmSummaryPublishIntervalSeconds, "redis_swarm_publish_invalid");
            RequirePositive(issues, "redis.swarmSummaryTtlSeconds", configuration.Redis.SwarmSummaryTtlSeconds, "redis_swarm_ttl_invalid");

            if (configuration.Redis.OwnershipRefreshIntervalSeconds >= configuration.Redis.OwnershipLeaseDurationSeconds)
            {
                issues.Add(Error("redis_lease_refresh_conflict", "redis.ownershipRefreshIntervalSeconds", "Ownership refresh interval must be less than ownership lease duration."));
            }
        }

        if (configuration.Postgres.Enabled)
        {
            Require(issues, "postgres.connectionString", configuration.Postgres.ConnectionString, "postgres_connection_required", "PostgreSQL connection string is required when PostgreSQL persistence is enabled.");
            RequirePositive(issues, "postgres.telemetryBatchSize", configuration.Postgres.TelemetryBatchSize, "postgres_batch_size_invalid");
            RequirePositive(issues, "postgres.telemetryFlushIntervalMilliseconds", configuration.Postgres.TelemetryFlushIntervalMilliseconds, "postgres_flush_interval_invalid");
        }

        RequirePositive(issues, "abuse.maxAnnounceQueryLength", configuration.AbuseProtection.MaxAnnounceQueryLength, "abuse_announce_query_length_invalid");
        RequirePositive(issues, "abuse.maxScrapeQueryLength", configuration.AbuseProtection.MaxScrapeQueryLength, "abuse_scrape_query_length_invalid");
        RequirePositive(issues, "abuse.maxQueryParameterCount", configuration.AbuseProtection.MaxQueryParameterCount, "abuse_max_query_parameter_count_invalid");
        RequirePositive(issues, "abuse.hardMaxNumWant", configuration.AbuseProtection.HardMaxNumWant, "abuse_hard_max_numwant_invalid");
        RequirePositive(issues, "abuse.maxScrapeInfoHashes", configuration.AbuseProtection.MaxScrapeInfoHashes, "abuse_max_scrape_hashes_invalid");

        if (configuration.Observability.EnableHealthEndpoints)
        {
            Require(issues, "observability.liveRoute", configuration.Observability.LiveRoute, "observability_live_route_required", "Live health route is required when health endpoints are enabled.");
            Require(issues, "observability.readyRoute", configuration.Observability.ReadyRoute, "observability_ready_route_required", "Ready health route is required when health endpoints are enabled.");
            Require(issues, "observability.startupRoute", configuration.Observability.StartupRoute, "observability_startup_route_required", "Startup health route is required when health endpoints are enabled.");
        }

        var routeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ValidateDistinctRoute(issues, routeSet, "http.announceRoute", configuration.Http.AnnounceRoute);
        ValidateDistinctRoute(issues, routeSet, "http.privateAnnounceRoute", configuration.Http.PrivateAnnounceRoute);
        ValidateDistinctRoute(issues, routeSet, "http.scrapeRoute", configuration.Http.ScrapeRoute);
        ValidateDistinctRoute(issues, routeSet, "observability.liveRoute", configuration.Observability.LiveRoute);
        ValidateDistinctRoute(issues, routeSet, "observability.readyRoute", configuration.Observability.ReadyRoute);
        ValidateDistinctRoute(issues, routeSet, "observability.startupRoute", configuration.Observability.StartupRoute);

        return new TrackerNodeConfigurationValidationResultDto(!issues.Any(static issue => issue.Severity == TrackerConfigValidationSeverity.Error), issues);
    }

    private static void ValidateDistinctRoute(ICollection<TrackerConfigValidationIssueDto> issues, ISet<string> routes, string path, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!routes.Add(value))
        {
            issues.Add(Error("route_conflict", path, $"Route '{value}' collides with another configured surface."));
        }
    }

    private static void Require(ICollection<TrackerConfigValidationIssueDto> issues, string path, string? value, string code, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(Error(code, path, message));
        }
    }

    private static void RequirePositive(ICollection<TrackerConfigValidationIssueDto> issues, string path, int value, string code)
    {
        if (value <= 0)
        {
            issues.Add(Error(code, path, $"{path} must be positive."));
        }
    }

    private static void RequirePort(ICollection<TrackerConfigValidationIssueDto> issues, string path, int value, string code)
    {
        if (value is <= 0 or > 65535)
        {
            issues.Add(Error(code, path, $"{path} must be between 1 and 65535."));
        }
    }

    private static void RequireAbsoluteUri(ICollection<TrackerConfigValidationIssueDto> issues, string path, string value, string code)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            issues.Add(Error(code, path, $"{path} must be a valid absolute URI."));
        }
    }

    private static TrackerConfigValidationIssueDto Error(string code, string path, string message)
        => new(code, path, TrackerConfigValidationSeverity.Error, message);
}
