using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Tracker.Gateway.Application.Announce;
using Tracker.Gateway.Infrastructure;

namespace Tracker.Gateway.IntegrationTests;

public sealed class AnnounceEndpointIntegrationTests : IAsyncLifetime
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
    private string _postgresConnectionString = string.Empty;
    private string _redisConnectionString = string.Empty;

    [Fact]
    public async Task Announce_ReturnsCompactPeers_WhenAccessSnapshotsAreHydrated()
    {
        await HydrateSnapshotsAsync();

        using var request = CreateAnnounceRequest();
        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("8:interval", body);
        Assert.Contains("5:peers", body);
        Assert.Contains("8:completei0e", body);
        Assert.Contains("10:incompletei1e", body);
    }

    [Fact]
    public async Task Announce_ReturnsPeers6_WhenRequesterIsIpv6()
    {
        await HydrateSnapshotsAsync();

        using var seedRequest = CreateAnnounceRequest(peerIdHex: "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB", ipv6: "2001:db8::22");
        await _client.SendAsync(seedRequest);

        using var request = CreateAnnounceRequest(peerIdHex: "AABBCCDDEEAABBCCDDEEAABBCCDDEEAABBCCDDEE", ipv6: "2001:db8::11");
        var response = await _client.SendAsync(request);
        var payload = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.True(ContainsSubsequence(payload, System.Text.Encoding.ASCII.GetBytes("6:peers6")));
    }

    [Fact]
    public async Task Announce_ReturnsFailure_WhenUserIsBanned()
    {
        await UpsertBanAsync("user", "00000000-0000-0000-0000-000000000002", "banned user");
        await HydrateSnapshotsAsync();

        using var request = CreateAnnounceRequest();
        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("14:failure reason", body);
        Assert.Contains("banned user", body);
    }

    [Fact]
    public async Task Scrape_ReturnsBencodedFiles_ForPublicTorrent()
    {
        await HydrateSnapshotsAsync();

        using var request = CreateScrapeRequest("/scrape?info_hash=%BB%BB%BB%BB%BB%BB%BB%BB%BB%BB%BB%BB%BB%BB%BB%BB%BB%BB%BB%BB");
        var response = await _client.SendAsync(request);
        var payload = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.True(ContainsSubsequence(payload, System.Text.Encoding.ASCII.GetBytes("5:files")));
        Assert.True(ContainsSubsequence(payload, System.Text.Encoding.ASCII.GetBytes("10:downloadedi0e")));
    }

    [Fact]
    public async Task Scrape_ReturnsBencodedFiles_ForPrivateTorrentWithPasskey()
    {
        await HydrateSnapshotsAsync();

        using var request = CreateScrapeRequest("/scrape/bootstrap-passkey?info_hash=%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA");
        var response = await _client.SendAsync(request);
        var payload = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.True(ContainsSubsequence(payload, System.Text.Encoding.ASCII.GetBytes("5:files")));
    }

    [Fact]
    public async Task Scrape_Fails_WhenPrivateTorrentHasNoPasskey()
    {
        await HydrateSnapshotsAsync();

        using var request = CreateScrapeRequest("/scrape?info_hash=%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA");
        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("14:failure reason", body);
    }

    [Fact]
    public async Task Scrape_RespectsAllowScrapeFalse()
    {
        await HydrateSnapshotsAsync();

        using var request = CreateScrapeRequest("/scrape/bootstrap-passkey?info_hash=%CC%CC%CC%CC%CC%CC%CC%CC%CC%CC%CC%CC%CC%CC%CC%CC%CC%CC%CC%CC");
        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("14:failure reason", body);
    }

    [Fact]
    public async Task Scrape_OmitsDeniedEntries_WhenMultiInfoHashContainsMixedAccess()
    {
        await HydrateSnapshotsAsync();

        using var request = CreateScrapeRequest("/scrape/bootstrap-passkey?info_hash=%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA&info_hash=%CC%CC%CC%CC%CC%CC%CC%CC%CC%CC%CC%CC%CC%CC%CC%CC%CC%CC%CC%CC");
        var response = await _client.SendAsync(request);
        var payload = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.True(ContainsSubsequence(payload, Convert.FromHexString("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")));
        Assert.False(ContainsSubsequence(payload, Convert.FromHexString("CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC")));
    }

    [Fact]
    public async Task Announce_RejectsQueryPasskeyRouting_WhenDisabled()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/announce?passkey=bootstrap-passkey&info_hash=%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA&peer_id=-UT0001-123456789012&port=6881&uploaded=0&downloaded=0&left=1&compact=1&numwant=10");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "127.0.0.1");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "http");

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("passkey query routing is disabled", body);
    }

    [Fact]
    public async Task Announce_RejectsOversizedQuery()
    {
        var oversized = new string('A', 2100);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/announce/bootstrap-passkey?info_hash=%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA&peer_id=-UT0001-123456789012&port=6881&uploaded=0&downloaded=0&left=1&compact=1&numwant=10&padding={oversized}");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "127.0.0.1");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "http");

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("query too large", body);
    }

    [Fact]
    public async Task Announce_ReturnsBencodedFailure_WhenUnhandledExceptionOccurs()
    {
        await using var factory = new GatewayApiFactory(_postgresConnectionString, _redisConnectionString, services =>
        {
            services.RemoveAll<IAnnounceService>();
            services.AddSingleton<IAnnounceService, ThrowingAnnounceService>();
        });

        using var client = factory.CreateClient();
        using var request = CreateAnnounceRequest();
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("14:failure reason", body);
        Assert.Contains("internal server error", body);
    }

    public async Task InitializeAsync()
    {
        await _postgresContainer.StartAsync();
        await _redisContainer.StartAsync();

        _postgresConnectionString = _postgresContainer.GetConnectionString();
        _redisConnectionString = _redisContainer.GetConnectionString();

        await InitializeDatabaseAsync();

        _factory = new GatewayApiFactory(_postgresConnectionString, _redisConnectionString);
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgresContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }

    private async Task HydrateSnapshotsAsync()
    {
        var provider = _factory.Services.GetRequiredService<IAccessSnapshotProvider>();

        await provider.GetTorrentPolicyAsync("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", CancellationToken.None);
        await provider.GetTorrentPolicyAsync("BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB", CancellationToken.None);
        await provider.GetTorrentPolicyAsync("CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC", CancellationToken.None);
        await provider.GetPasskeyAsync("bootstrap-passkey", CancellationToken.None);
        await provider.GetUserPermissionAsync(Guid.Parse("00000000-0000-0000-0000-000000000002"), CancellationToken.None);
        await provider.GetBanRuleAsync("user", "00000000-0000-0000-0000-000000000002", CancellationToken.None);

        await WaitUntilAsync(
            async () =>
            {
                var policy = await provider.GetTorrentPolicyAsync("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", CancellationToken.None);
                var publicPolicy = await provider.GetTorrentPolicyAsync("BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB", CancellationToken.None);
                var scrapeDisabledPolicy = await provider.GetTorrentPolicyAsync("CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC", CancellationToken.None);
                var passkey = await provider.GetPasskeyAsync("bootstrap-passkey", CancellationToken.None);
                var permissions = await provider.GetUserPermissionAsync(Guid.Parse("00000000-0000-0000-0000-000000000002"), CancellationToken.None);
                return policy is not null && publicPolicy is not null && scrapeDisabledPolicy is not null && passkey is not null && permissions is not null;
            },
            value => value,
            TimeSpan.FromSeconds(5));
    }

    private async Task UpsertBanAsync(string scope, string subject, string reason)
    {
        await using var connection = new NpgsqlConnection(_postgresConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bans (scope, subject, reason, expires_at_utc)
            values ($1, $2, $3, null)
            on conflict (scope, subject) do update
                set reason = excluded.reason,
                    expires_at_utc = excluded.expires_at_utc
            """;
        command.Parameters.Add(new NpgsqlParameter { Value = scope });
        command.Parameters.Add(new NpgsqlParameter { Value = subject });
        command.Parameters.Add(new NpgsqlParameter { Value = reason });
        await command.ExecuteNonQueryAsync();
    }

    private static HttpRequestMessage CreateAnnounceRequest(string? peerIdHex = null, string? ipv6 = null)
    {
        var peerId = peerIdHex is null
            ? "-UT0001-123456789012"
            : string.Concat(Convert.FromHexString(peerIdHex).Select(static b => $"%{b:X2}"));
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/announce/bootstrap-passkey?info_hash=%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA%AA&peer_id={peerId}&port=6881&uploaded=0&downloaded=0&left=1&compact=1&numwant=10&event=started");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", ipv6 ?? "127.0.0.1");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "http");
        return request;
    }

    private static HttpRequestMessage CreateScrapeRequest(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "127.0.0.1");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "http");
        return request;
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

            insert into torrents (id, info_hash, is_private, is_enabled)
            values ('00000000-0000-0000-0000-000000000001', 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA', true, true)
            on conflict (info_hash) do nothing;

            insert into torrents (id, info_hash, is_private, is_enabled)
            values ('00000000-0000-0000-0000-000000000003', 'BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB', false, true)
            on conflict (info_hash) do nothing;

            insert into torrents (id, info_hash, is_private, is_enabled)
            values ('00000000-0000-0000-0000-000000000004', 'CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC', true, true)
            on conflict (info_hash) do nothing;

            insert into torrent_policies (torrent_id, announce_interval_seconds, min_announce_interval_seconds, default_numwant, max_numwant, allow_scrape)
            values ('00000000-0000-0000-0000-000000000001', 1800, 900, 50, 80, true)
            on conflict (torrent_id) do nothing;

            insert into torrent_policies (torrent_id, announce_interval_seconds, min_announce_interval_seconds, default_numwant, max_numwant, allow_scrape)
            values ('00000000-0000-0000-0000-000000000003', 1800, 900, 50, 80, true)
            on conflict (torrent_id) do nothing;

            insert into torrent_policies (torrent_id, announce_interval_seconds, min_announce_interval_seconds, default_numwant, max_numwant, allow_scrape)
            values ('00000000-0000-0000-0000-000000000004', 1800, 900, 50, 80, false)
            on conflict (torrent_id) do nothing;

            insert into passkeys (passkey, user_id, is_revoked, expires_at_utc)
            values ('bootstrap-passkey', '00000000-0000-0000-0000-000000000002', false, null)
            on conflict (passkey) do nothing;

            insert into permissions (user_id, can_leech, can_seed, can_scrape, can_use_private_tracker)
            values ('00000000-0000-0000-0000-000000000002', true, true, true, true)
            on conflict (user_id) do nothing;

            delete from bans;
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

    private static bool ContainsSubsequence(ReadOnlySpan<byte> source, ReadOnlySpan<byte> value)
    {
        return source.IndexOf(value) >= 0;
    }

    private sealed class GatewayApiFactory(
        string postgresConnectionString,
        string redisConnectionString,
        Action<IServiceCollection>? configureTestServices = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{PostgresOptions.SectionName}:ConnectionString"] = postgresConnectionString,
                    [$"{RedisOptions.SectionName}:Configuration"] = redisConnectionString,
                    [$"{TrackerNodeOptions.SectionName}:NodeId"] = "http-integration-node",
                    [$"{TrackerNodeOptions.SectionName}:Region"] = "integration",
                    [$"{TelemetryBatchingOptions.SectionName}:BatchSize"] = "10",
                    [$"{TelemetryBatchingOptions.SectionName}:FlushIntervalMilliseconds"] = "1000",
                    [$"{PolicyCacheOptions.SectionName}:L1Seconds"] = "60",
                    [$"{PolicyCacheOptions.SectionName}:L2Seconds"] = "300",
                    [$"{TrackerSecurityOptions.SectionName}:AllowIPv6Peers"] = "true"
                });
            });

            if (configureTestServices is not null)
            {
                builder.ConfigureServices(configureTestServices);
            }
        }
    }

    private sealed class ThrowingAnnounceService : IAnnounceService
    {
        public ValueTask<(AnnounceSuccess? Success, AnnounceError? Error)> ExecuteAsync(AnnounceRequest request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("simulated runtime failure");
        }
    }
}
