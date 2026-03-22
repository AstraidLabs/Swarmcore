using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using StackExchange.Redis;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using Swarmcore.Contracts.Configuration;
using Swarmcore.Hosting;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Tracker.ConfigurationService.Application;
using Tracker.ConfigurationService.Infrastructure;
using Tracker.Gateway.Application.Announce;
using Tracker.Gateway.Infrastructure;

namespace Tracker.Gateway.IntegrationTests;

public sealed class AccessSnapshotIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("swarmcore")
        .WithUsername("swarmcore")
        .WithPassword("swarmcore")
        .Build();

    private readonly RedisContainer _redisContainer = new RedisBuilder("redis:7.4")
        .Build();

    private IHost _host = null!;
    private string _postgresConnectionString = string.Empty;
    private string _redisConnectionString = string.Empty;

    [Fact]
    public async Task HydrationService_RefillsRedisAndL1_FromPostgres()
    {
        var provider = _host.Services.GetRequiredService<IAccessSnapshotProvider>();
        await using var redis = await ConnectionMultiplexer.ConnectAsync(_redisConnectionString);

        var first = await provider.GetTorrentPolicyAsync("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", CancellationToken.None);
        Assert.Null(first);

        var hydrated = await WaitUntilAsync(
            async () => await provider.GetTorrentPolicyAsync("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", CancellationToken.None),
            value => value is not null,
            TimeSpan.FromSeconds(5));

        Assert.NotNull(hydrated);
        Assert.True(hydrated!.IsPrivate);

        var cached = await redis.GetDatabase().StringGetAsync("tracker:policy:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        Assert.True(cached.HasValue);
    }

    [Fact]
    public async Task ConfigurationMutation_InvalidatesGatewayL1Snapshots()
    {
        var provider = _host.Services.GetRequiredService<IAccessSnapshotProvider>();
        var mutationService = _host.Services.GetRequiredService<IConfigurationMutationService>();

        await provider.GetPasskeyAsync("bootstrap-passkey", CancellationToken.None);
        var hydratedPasskey = await WaitUntilAsync(
            async () => await provider.GetPasskeyAsync("bootstrap-passkey", CancellationToken.None),
            value => value is not null,
            TimeSpan.FromSeconds(5));
        Assert.NotNull(hydratedPasskey);

        await provider.GetUserPermissionAsync(Guid.Parse("00000000-0000-0000-0000-000000000002"), CancellationToken.None);
        var hydratedPermissions = await WaitUntilAsync(
            async () => await provider.GetUserPermissionAsync(Guid.Parse("00000000-0000-0000-0000-000000000002"), CancellationToken.None),
            value => value is not null,
            TimeSpan.FromSeconds(5));
        Assert.NotNull(hydratedPermissions);

        await mutationService.UpsertPasskeyAsync(
            "bootstrap-passkey",
            new PasskeyUpsertRequest(Guid.Parse("00000000-0000-0000-0000-000000000002"), true, null),
            new AdminMutationContext("integration-test", "operator", "corr-passkey", null, null, null),
            CancellationToken.None);

        await mutationService.UpsertUserPermissionsAsync(
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            new UserPermissionUpsertRequest(false, false, true, true),
            new AdminMutationContext("integration-test", "operator", "corr-permissions", null, null, null),
            CancellationToken.None);

        var passkeyInvalidated = await WaitUntilAsync(
            async () => await provider.GetPasskeyAsync("bootstrap-passkey", CancellationToken.None) is null,
            value => value,
            TimeSpan.FromSeconds(5));

        var permissionInvalidated = await WaitUntilAsync(
            async () => await provider.GetUserPermissionAsync(Guid.Parse("00000000-0000-0000-0000-000000000002"), CancellationToken.None) is null,
            value => value,
            TimeSpan.FromSeconds(5));

        Assert.True(passkeyInvalidated);
        Assert.True(permissionInvalidated);
    }

    public async Task InitializeAsync()
    {
        await _postgresContainer.StartAsync();
        await _redisContainer.StartAsync();

        _postgresConnectionString = _postgresContainer.GetConnectionString();
        _redisConnectionString = _redisContainer.GetConnectionString();

        await InitializeDatabaseAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{PostgresOptions.SectionName}:ConnectionString"] = _postgresConnectionString,
                [$"{RedisOptions.SectionName}:Configuration"] = _redisConnectionString,
                [$"{TrackerNodeOptions.SectionName}:NodeId"] = "integration-node",
                [$"{TrackerNodeOptions.SectionName}:Region"] = "integration",
                [$"{TelemetryBatchingOptions.SectionName}:BatchSize"] = "10",
                [$"{TelemetryBatchingOptions.SectionName}:FlushIntervalMilliseconds"] = "1000",
                [$"{PolicyCacheOptions.SectionName}:L1Seconds"] = "60",
                [$"{PolicyCacheOptions.SectionName}:L2Seconds"] = "300"
            })
            .Build();

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddConfiguration(configuration);
        builder.Services.AddSwarmcoreInfrastructure(builder.Configuration, usePostgres: true, useRedis: true);
        builder.Services.AddGatewayInfrastructure();
        builder.Services.AddConfigurationInfrastructure(builder.Configuration);

        _host = builder.Build();
        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        await _postgresContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }

    private async Task InitializeDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(_postgresConnectionString);
        await connection.OpenAsync();

        var sql =
            """
            create table if not exists torrents (
                id uuid primary key,
                info_hash varchar(40) not null unique,
                is_private boolean not null,
                is_enabled boolean not null
            );

            create table if not exists torrent_policies (
                torrent_id uuid primary key references torrents(id),
                announce_interval_seconds integer not null,
                min_announce_interval_seconds integer not null,
                default_numwant integer not null,
                max_numwant integer not null,
                allow_scrape boolean not null,
                row_version bigint not null default 1,
                warning_message varchar(512) null
            );

            create table if not exists passkeys (
                passkey varchar(128) primary key,
                user_id uuid not null,
                is_revoked boolean not null,
                expires_at_utc timestamp without time zone null,
                row_version bigint not null default 1
            );

            create table if not exists permissions (
                user_id uuid primary key,
                can_leech boolean not null,
                can_seed boolean not null,
                can_scrape boolean not null,
                can_use_private_tracker boolean not null,
                row_version bigint not null default 1
            );

            create table if not exists bans (
                scope varchar(32) not null,
                subject varchar(128) not null,
                reason text not null,
                expires_at_utc timestamp without time zone null,
                row_version bigint not null default 1,
                primary key (scope, subject)
            );

            create table if not exists announce_telemetry (
                id bigserial primary key,
                node_id text not null,
                info_hash text not null,
                peer_id text not null,
                passkey text null,
                event_name text not null,
                requested_peers integer not null,
                returned_peers integer not null,
                occurred_at_utc timestamp without time zone not null
            );

            create table if not exists audit_records (
                id uuid primary key,
                occurred_at_utc timestamp without time zone not null,
                actor_id varchar(256) not null,
                actor_role varchar(64) not null,
                action varchar(128) not null,
                severity varchar(32) not null,
                entity_type varchar(64) not null,
                entity_id varchar(256) not null,
                correlation_id varchar(128) not null,
                request_id varchar(128) null,
                result varchar(32) not null,
                ip_address varchar(128) null,
                user_agent varchar(512) null,
                before_json jsonb null,
                after_json jsonb null
            );

            create table if not exists maintenance_runs (
                id uuid primary key,
                operation varchar(128) not null,
                requested_by varchar(256) not null,
                requested_at_utc timestamp without time zone not null,
                status varchar(32) not null,
                correlation_id varchar(128) not null
            );

            insert into torrents (id, info_hash, is_private, is_enabled)
            values ('00000000-0000-0000-0000-000000000001', 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA', true, true)
            on conflict (info_hash) do nothing;

            insert into torrent_policies (torrent_id, announce_interval_seconds, min_announce_interval_seconds, default_numwant, max_numwant, allow_scrape)
            values ('00000000-0000-0000-0000-000000000001', 1800, 900, 50, 80, true)
            on conflict (torrent_id) do nothing;

            insert into passkeys (passkey, user_id, is_revoked, expires_at_utc)
            values ('bootstrap-passkey', '00000000-0000-0000-0000-000000000002', false, null)
            on conflict (passkey) do nothing;

            insert into permissions (user_id, can_leech, can_seed, can_scrape, can_use_private_tracker)
            values ('00000000-0000-0000-0000-000000000002', true, true, true, true)
            on conflict (user_id) do nothing;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> WaitUntilAsync<T>(Func<Task<T>> action, Func<T, bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var value = await action();
            if (predicate(value))
            {
                return value;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Condition was not met within the allotted time.");
    }
}
