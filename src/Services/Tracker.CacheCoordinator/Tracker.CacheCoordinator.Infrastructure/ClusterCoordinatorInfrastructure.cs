using System.Collections.Frozen;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BeeTracker.BuildingBlocks.Abstractions.Options;
using BeeTracker.BuildingBlocks.Observability.Diagnostics;
using StackExchange.Redis;
using BeeTracker.Caching.Redis;
using BeeTracker.Contracts.Runtime;
using Tracker.CacheCoordinator.Application;

namespace Tracker.CacheCoordinator.Infrastructure;

// ─── Redis Key Scheme ─────────────────────────────────────────────────────────

/// <summary>
/// Canonical Redis key scheme for cluster coordination.
/// Both gateway and coordinator must agree on these patterns.
/// </summary>
internal static class ClusterRedisKeys
{
    public static string OwnershipKey(int shardId) => $"cluster:shard:{shardId}:owner";
    public static string NodeStateKey(string nodeId) => $"cluster:node:state:{nodeId}";
}

// ─── Shard Ownership Registry ─────────────────────────────────────────────────

/// <summary>
/// Redis-backed shard ownership registry.
///
/// Ownership model:
/// - Each shard has a Redis key: cluster:shard:{shardId}:owner
/// - The key value is a JSON ShardOwnershipRecord
/// - The key TTL equals the lease duration
/// - A node "owns" a shard as long as its key exists and the OwnerNodeId matches
///
/// Claim semantics (TryClaim):
/// - Use SET NX (set-if-not-exists) to claim an unowned shard atomically
/// - If the key already exists, read it and return the existing record
///
/// Refresh semantics (TryRefresh):
/// - Check the current owner; if still this node, extend the TTL with SET XX (set-if-exists)
/// - Returns false if the record no longer belongs to this node
///
/// Release semantics (TryRelease):
/// - Compare-and-delete: read key, verify owner matches, then delete
/// - Uses a Lua script for atomicity
/// </summary>
public sealed class RedisShardOwnershipRegistry(
    IRedisCacheClient redisCacheClient,
    ILogger<RedisShardOwnershipRegistry> logger) : IShardOwnershipRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Lua script: delete the key only if the current OwnerNodeId matches
    private const string ReleaseScript = """
        local val = redis.call('GET', KEYS[1])
        if val then
            local rec = cjson.decode(val)
            if rec['OwnerNodeId'] == ARGV[1] then
                redis.call('DEL', KEYS[1])
                return 1
            end
        end
        return 0
        """;

    public async Task<ShardOwnershipRecord?> TryClaimAsync(
        int shardId, string nodeId, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        var key = ClusterRedisKeys.OwnershipKey(shardId);
        var record = new ShardOwnershipRecord(
            shardId,
            nodeId,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.Add(leaseDuration),
            Epoch: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        var json = JsonSerializer.Serialize(record, JsonOptions);

        // SET NX — only succeeds if the key does not exist
        var claimed = await redisCacheClient.Database.StringSetAsync(
            key, json, leaseDuration, When.NotExists);

        if (claimed)
        {
            return record;
        }

        // Key already exists — read and return existing owner
        return await GetOwnershipAsync(shardId, cancellationToken);
    }

    public async Task<bool> TryRefreshAsync(
        int shardId, string nodeId, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        var key = ClusterRedisKeys.OwnershipKey(shardId);

        // Read current record to verify ownership before extending TTL
        var current = await GetOwnershipAsync(shardId, cancellationToken);
        if (current is null || !current.OwnerNodeId.Equals(nodeId, StringComparison.Ordinal))
        {
            return false;
        }

        // Write updated record with new expiry, only if key still exists (XX flag)
        var updated = current with
        {
            LeaseExpiresAtUtc = DateTimeOffset.UtcNow.Add(leaseDuration)
        };

        var json = JsonSerializer.Serialize(updated, JsonOptions);
        return await redisCacheClient.Database.StringSetAsync(
            key, json, leaseDuration, When.Exists);
    }

    public async Task<bool> TryReleaseAsync(int shardId, string nodeId, CancellationToken cancellationToken)
    {
        var key = ClusterRedisKeys.OwnershipKey(shardId);

        var result = await redisCacheClient.Database.ScriptEvaluateAsync(
            ReleaseScript,
            [(RedisKey)key],
            [(RedisValue)nodeId]);

        return (long)result == 1;
    }

    public async Task<ShardOwnershipRecord?> GetOwnershipAsync(int shardId, CancellationToken cancellationToken)
    {
        var key = ClusterRedisKeys.OwnershipKey(shardId);
        var value = await redisCacheClient.Database.StringGetAsync(key);
        if (!value.HasValue)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ShardOwnershipRecord>(value.ToString(), JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to deserialize ownership record for shard {ShardId}; treating as unowned.", shardId);
            return null;
        }
    }

    public async Task<IReadOnlyDictionary<int, ShardOwnershipRecord>> GetAllOwnershipsAsync(
        int totalShardCount, CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, ShardOwnershipRecord>(totalShardCount);

        for (var shardId = 0; shardId < totalShardCount; shardId++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = await GetOwnershipAsync(shardId, cancellationToken);
            if (record is not null)
            {
                result[shardId] = record;
            }
        }

        return result.ToFrozenDictionary();
    }
}

// ─── Shard Ownership Service ──────────────────────────────────────────────────

/// <summary>
/// Manages this node's shard ownership lifecycle.
///
/// Startup: claims all unclaimed shards and all shards whose leases have expired
///          (failover — the previous owner's heartbeat has lapsed).
/// Refresh: extends lease TTL for all owned shards every OwnershipRefreshIntervalSeconds.
/// Drain:   releases all owned shards so other nodes can reclaim them immediately.
///
/// Thread safety: OwnedShardIds is updated atomically via Interlocked replacement
/// of the backing ImmutableHashSet reference.
/// </summary>
public sealed class ShardOwnershipService(
    IShardOwnershipRegistry ownershipRegistry,
    IOptions<ClusterShardingOptions> shardingOptions,
    IOptions<TrackerNodeOptions> nodeOptions,
    ILogger<ShardOwnershipService> logger) : IShardOwnershipService
{
    private readonly int _totalShards = shardingOptions.Value.ClusterShardCount;
    private readonly TimeSpan _leaseDuration = TimeSpan.FromSeconds(shardingOptions.Value.OwnershipLeaseDurationSeconds);
    private readonly string _nodeId = nodeOptions.Value.NodeId;

    // Lock-free snapshot pattern: replace the whole set atomically
    private volatile FrozenSet<int> _ownedShards = FrozenSet<int>.Empty;

    public IReadOnlySet<int> OwnedShardIds => _ownedShards;

    public async Task<int> ClaimAvailableShardsAsync(CancellationToken cancellationToken)
    {
        var claimed = 0;
        var newOwned = new HashSet<int>(_ownedShards);

        for (var shardId = 0; shardId < _totalShards; shardId++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Check if already owned by us
            if (newOwned.Contains(shardId))
            {
                continue;
            }

            var existing = await ownershipRegistry.GetOwnershipAsync(shardId, cancellationToken);
            if (existing is not null && existing.OwnerNodeId.Equals(_nodeId, StringComparison.Ordinal))
            {
                // We already own this shard (e.g., after restart)
                newOwned.Add(shardId);
                continue;
            }

            if (existing is not null)
            {
                // Shard is owned by another node; check if lease has expired
                // (Redis TTL expiry handles this — if the key still exists, the lease is valid)
                continue;
            }

            // Shard is unowned (key expired or never claimed) — try to claim it
            var result = await ownershipRegistry.TryClaimAsync(shardId, _nodeId, _leaseDuration, cancellationToken);
            if (result is not null && result.OwnerNodeId.Equals(_nodeId, StringComparison.Ordinal))
            {
                newOwned.Add(shardId);
                claimed++;

                TrackerDiagnostics.ClusterOwnershipClaims.Add(1,
                    new KeyValuePair<string, object?>("node_id", _nodeId),
                    new KeyValuePair<string, object?>("shard_id", shardId),
                    new KeyValuePair<string, object?>("reason", "available"));

                logger.LogInformation("Claimed shard {ShardId}.", shardId);
            }
            else
            {
                TrackerDiagnostics.ClusterOwnershipClaimFailures.Add(1,
                    new KeyValuePair<string, object?>("shard_id", shardId));
            }
        }

        if (claimed > 0)
        {
            _ownedShards = newOwned.ToFrozenSet();
        }

        return claimed;
    }

    public async Task<int> RefreshOwnedShardsAsync(CancellationToken cancellationToken)
    {
        var lostCount = 0;
        var currentOwned = new HashSet<int>(_ownedShards);
        var toRemove = new List<int>();

        foreach (var shardId in currentOwned)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var refreshed = await ownershipRegistry.TryRefreshAsync(shardId, _nodeId, _leaseDuration, cancellationToken);
            if (!refreshed)
            {
                toRemove.Add(shardId);
                lostCount++;

                TrackerDiagnostics.ClusterOwnershipRefreshFailures.Add(1,
                    new KeyValuePair<string, object?>("shard_id", shardId));
                TrackerDiagnostics.ClusterOwnershipTransitions.Add(1,
                    new KeyValuePair<string, object?>("reason", "refresh_failed"));

                logger.LogWarning("Lost ownership of shard {ShardId} during refresh (lease expired or reclaimed).", shardId);
            }
        }

        if (toRemove.Count > 0)
        {
            currentOwned.ExceptWith(toRemove);
            _ownedShards = currentOwned.ToFrozenSet();
        }

        return lostCount;
    }

    public async Task ReleaseAllShardsAsync(CancellationToken cancellationToken)
    {
        var owned = _ownedShards;
        var released = 0;

        foreach (var shardId in owned)
        {
            try
            {
                var ok = await ownershipRegistry.TryReleaseAsync(shardId, _nodeId, cancellationToken);
                if (ok)
                {
                    released++;
                    TrackerDiagnostics.ClusterOwnershipReleases.Add(1,
                        new KeyValuePair<string, object?>("shard_id", shardId));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to release shard {ShardId} during drain.", shardId);
            }
        }

        _ownedShards = FrozenSet<int>.Empty;
        logger.LogInformation("Released {Released}/{Total} owned shards.", released, owned.Count);
    }
}

// ─── Node Operational State Service ──────────────────────────────────────────

public sealed class NodeOperationalStateService(
    IRedisCacheClient redisCacheClient,
    IOptions<TrackerNodeOptions> nodeOptions,
    ILogger<NodeOperationalStateService> logger) : INodeOperationalStateService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan StateTtl = TimeSpan.FromHours(24);
    private volatile NodeOperationalState _currentState = NodeOperationalState.Active;
    private readonly string _nodeId = nodeOptions.Value.NodeId;

    public NodeOperationalState CurrentState => _currentState;

    public async Task TransitionToAsync(NodeOperationalState newState, CancellationToken cancellationToken)
    {
        var previous = _currentState;
        if (previous == newState)
        {
            return;
        }

        _currentState = newState;

        var dto = new NodeOperationalStateDto(_nodeId, newState, DateTimeOffset.UtcNow);
        var json = JsonSerializer.Serialize(dto, JsonOptions);
        await redisCacheClient.Database.StringSetAsync(
            ClusterRedisKeys.NodeStateKey(_nodeId), json, StateTtl);

        TrackerDiagnostics.ClusterNodeStateChanges.Add(1,
            new KeyValuePair<string, object?>("from", previous.ToString()),
            new KeyValuePair<string, object?>("to", newState.ToString()));

        logger.LogInformation("Node {NodeId} transitioned from {From} to {To}.", _nodeId, previous, newState);
    }
}

// ─── Service Registration ─────────────────────────────────────────────────────

public static class ClusterCoordinatorInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddClusterCoordinatorInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IShardOwnershipRegistry, RedisShardOwnershipRegistry>();
        services.AddSingleton<IShardOwnershipService, ShardOwnershipService>();
        services.AddSingleton<INodeOperationalStateService, NodeOperationalStateService>();
        return services;
    }
}
