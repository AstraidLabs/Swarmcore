using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Swarmcore.SmokeTests;

public sealed class GatewayStartupSmokeTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("swarmcore")
        .WithUsername("swarmcore")
        .WithPassword("swarmcore")
        .Build();

    private readonly RedisContainer _redisContainer = new RedisBuilder("redis:7.4")
        .Build();

    private GatewayApiFactory _factory = null!;
    private HttpClient _client = null!;

    // ─── Health endpoint smoke tests ─────────────────────────────────────────────

    [Fact]
    public async Task HealthLive_ReturnsOk()
    {
        var response = await _client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthStartup_ReturnsOk_WhenServiceIsReady()
    {
        var response = await _client.GetAsync("/health/startup");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("started", body);
    }

    [Fact]
    public async Task HealthReady_ReturnsOk_WhenDependenciesAreHealthy()
    {
        var response = await _client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ready", body);
    }

    // ─── Announce endpoint reachability ──────────────────────────────────────────

    [Fact]
    public async Task Announce_EndpointIsReachable()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/announce");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "127.0.0.1");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "http");

        var response = await _client.SendAsync(request);

        // Should return a tracker-protocol response (bencode), not a 404
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Announce_WithMissingParams_ReturnsBencodedFailure()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/announce?compact=1");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "127.0.0.1");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "http");

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        // Even on validation errors, tracker returns bencode failure response
        Assert.Contains("14:failure reason", body);
    }

    // ─── Scrape endpoint reachability ────────────────────────────────────────────

    [Fact]
    public async Task Scrape_EndpointIsReachable()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/scrape");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "127.0.0.1");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "http");

        var response = await _client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ─── Admin diagnostic endpoints ──────────────────────────────────────────────

    [Fact]
    public async Task AdminOverview_ReturnsOk()
    {
        var response = await _client.GetAsync("/admin/overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("smoke-test-node", body);
    }

    [Fact]
    public async Task AdminGovernance_ReturnsOk()
    {
        var response = await _client.GetAsync("/admin/governance");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AdminConfigValidate_ReturnsOk()
    {
        var response = await _client.GetAsync("/admin/config/validate");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("isValid", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdminClusterShards_ReturnsOk()
    {
        var response = await _client.GetAsync("/admin/cluster/shards");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AdminAbuseDiagnostics_ReturnsOk()
    {
        var response = await _client.GetAsync("/admin/abuse/diagnostics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AdminDiagnostics_ReturnsOk()
    {
        var response = await _client.GetAsync("/admin/diagnostics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ─── Lifecycle ───────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        await _postgresContainer.StartAsync();
        await _redisContainer.StartAsync();

        var postgresConnectionString = _postgresContainer.GetConnectionString();
        var redisConnectionString = _redisContainer.GetConnectionString();

        await InitializeDatabaseAsync(postgresConnectionString);

        _factory = new GatewayApiFactory(postgresConnectionString, redisConnectionString);
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgresContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }

    private static async Task InitializeDatabaseAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
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
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class GatewayApiFactory(
        string postgresConnectionString,
        string redisConnectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{PostgresOptions.SectionName}:ConnectionString"] = postgresConnectionString,
                    [$"{RedisOptions.SectionName}:Configuration"] = redisConnectionString,
                    [$"{TrackerNodeOptions.SectionName}:NodeId"] = "smoke-test-node",
                    [$"{TrackerNodeOptions.SectionName}:Region"] = "smoke",
                    [$"{TelemetryBatchingOptions.SectionName}:BatchSize"] = "10",
                    [$"{TelemetryBatchingOptions.SectionName}:FlushIntervalMilliseconds"] = "1000",
                    [$"{PolicyCacheOptions.SectionName}:L1Seconds"] = "60",
                    [$"{PolicyCacheOptions.SectionName}:L2Seconds"] = "300",
                });
            });
        }
    }
}
