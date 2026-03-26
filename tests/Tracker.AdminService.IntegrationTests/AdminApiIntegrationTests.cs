extern alias adminapi;

using System.Security.Cryptography;
using System.Text;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using StackExchange.Redis;
using BeeTracker.BuildingBlocks.Abstractions.Options;
using BeeTracker.Contracts.Configuration;
using BeeTracker.Contracts.Identity;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Tracker.ConfigurationService.Infrastructure;
using AdminProgram = adminapi::Program;

namespace Tracker.AdminService.IntegrationTests;

public sealed class AdminApiIntegrationTests : IAsyncLifetime
{
    private const string AdminApiScope = "admin_api";
    private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("beetracker")
        .WithUsername("beetracker")
        .WithPassword("beetracker")
        .Build();

    private readonly RedisContainer _redisContainer = new RedisBuilder("redis:7.4")
        .Build();

    private string _postgresConnectionString = string.Empty;
    private string _redisConnectionString = string.Empty;
    private AdminApiFactory _adminFactory = null!;
    private HttpClient _adminClient = null!;
    private string _csrfToken = string.Empty;

    [Fact]
    public async Task SessionEndpoint_ReturnsAnonymousShape_WhenNotAuthenticated()
    {
        using var anonymousClient = _adminFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });

        using var response = await anonymousClient.GetAsync("/api/admin/session");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(body);
        Assert.False(document.RootElement.GetProperty("isAuthenticated").GetBoolean());
        Assert.Equal(string.Empty, document.RootElement.GetProperty("userName").GetString());
        Assert.Equal(string.Empty, document.RootElement.GetProperty("role").GetString());
        Assert.Empty(document.RootElement.GetProperty("permissions").EnumerateArray());
        Assert.Equal(string.Empty, document.RootElement.GetProperty("csrfToken").GetString());
        Assert.True(document.RootElement.GetProperty("privilegedSessionFreshUntilUtc").ValueKind is JsonValueKind.Null);
        Assert.False(document.RootElement.GetProperty("requiresPrivilegedReauthentication").GetBoolean());
        Assert.Equal("/account/login?returnUrl=%2F&reauth=true", document.RootElement.GetProperty("reauthenticationUrl").GetString());
        var capabilities = document.RootElement.GetProperty("capabilities").EnumerateArray().ToArray();
        Assert.NotEmpty(capabilities);
        Assert.Contains(capabilities, static capability =>
            capability.GetProperty("action").GetString() == "admin.write.passkey" &&
            capability.GetProperty("httpMethod").GetString() == "PUT" &&
            capability.GetProperty("supportsBulk").GetBoolean() is false &&
            capability.GetProperty("bulkRoutePattern").ValueKind is JsonValueKind.Null &&
            capability.GetProperty("selectionMode").GetString() == "single" &&
            capability.GetProperty("idempotencyHint").GetString() == "idempotent" &&
            capability.GetProperty("confirmationRequired").GetBoolean() &&
            capability.GetProperty("confirmationSeverity").GetString() == "high" &&
            capability.GetProperty("dryRunSupported").GetBoolean() is false &&
            capability.GetProperty("granted").GetBoolean() is false &&
            capability.GetProperty("requiresPrivilegedReauthentication").GetBoolean() &&
            capability.GetProperty("severity").GetString() == "high" &&
            capability.GetProperty("category").GetString() == "access" &&
            capability.GetProperty("resourceKind").GetString() == "passkey" &&
            capability.GetProperty("routePattern").GetString() == "/api/admin/passkeys/{passkey}" &&
            capability.GetProperty("displayName").GetString() == "Manage passkeys" &&
            capability.GetProperty("reauthenticationPrompt").GetString() == "Sign in again to confirm this sensitive passkey change.");
        Assert.Contains(capabilities, static capability =>
            capability.GetProperty("action").GetString() == "admin.revoke.passkey" &&
            capability.GetProperty("httpMethod").GetString() == "POST" &&
            capability.GetProperty("supportsBulk").GetBoolean() &&
            capability.GetProperty("bulkRoutePattern").GetString() == "/api/admin/passkeys/bulk/revoke" &&
            capability.GetProperty("selectionMode").GetString() == "multi_select" &&
            capability.GetProperty("idempotencyHint").GetString() == "idempotent" &&
            capability.GetProperty("payloadKind").GetString() == "bulk_collection" &&
            capability.GetProperty("maxItems").GetInt32() == 200 &&
            capability.GetProperty("confirmationSeverity").GetString() == "high" &&
            capability.GetProperty("dryRunSupported").GetBoolean() is false &&
            capability.GetProperty("responseKind").GetString() == "bulk_operation_result" &&
            capability.GetProperty("resultItemKind").GetString() == "passkey" &&
            capability.GetProperty("resultCollectionProperty").GetString() == "passkeyItems" &&
            capability.GetProperty("requiredFields").EnumerateArray().Any(static field => field.GetString() == "passkey") &&
            capability.GetProperty("fieldTypes").GetProperty("expectedVersion").GetString() == "int64?");
        Assert.Contains(capabilities, static capability =>
            capability.GetProperty("action").GetString() == "admin.rotate.passkey" &&
            capability.GetProperty("httpMethod").GetString() == "POST" &&
            capability.GetProperty("supportsBulk").GetBoolean() &&
            capability.GetProperty("bulkRoutePattern").GetString() == "/api/admin/passkeys/bulk/rotate" &&
            capability.GetProperty("selectionMode").GetString() == "multi_select" &&
            capability.GetProperty("idempotencyHint").GetString() == "non_idempotent" &&
            capability.GetProperty("fieldTypes").GetProperty("expiresAtUtc").GetString() == "datetimeoffset?");
        Assert.Contains(capabilities, static capability =>
            capability.GetProperty("action").GetString() == "admin.bulk_upsert.torrent_policy" &&
            capability.GetProperty("httpMethod").GetString() == "PUT" &&
            capability.GetProperty("supportsBulk").GetBoolean() &&
            capability.GetProperty("bulkRoutePattern").GetString() == "/api/admin/torrents/bulk/policy" &&
            capability.GetProperty("dryRunRoutePattern").GetString() == "/api/admin/torrents/bulk/policy/dry-run" &&
            capability.GetProperty("payloadKind").GetString() == "bulk_collection" &&
            capability.GetProperty("maxItems").GetInt32() == 50 &&
            capability.GetProperty("confirmationSeverity").GetString() == "high" &&
            capability.GetProperty("dryRunSupported").GetBoolean() &&
            capability.GetProperty("responseKind").GetString() == "bulk_operation_result" &&
            capability.GetProperty("resultItemKind").GetString() == "torrent" &&
            capability.GetProperty("resultCollectionProperty").GetString() == "torrentItems" &&
            capability.GetProperty("requiredFields").EnumerateArray().Any(static field => field.GetString() == "infoHash") &&
            capability.GetProperty("fieldTypes").GetProperty("announceIntervalSeconds").GetString() == "int32");
        Assert.Contains(capabilities, static capability =>
            capability.GetProperty("action").GetString() == "admin.activate.torrent" &&
            capability.GetProperty("httpMethod").GetString() == "POST" &&
            capability.GetProperty("supportsBulk").GetBoolean() &&
            capability.GetProperty("bulkRoutePattern").GetString() == "/api/admin/torrents/bulk/activate" &&
            capability.GetProperty("payloadKind").GetString() == "bulk_collection" &&
            capability.GetProperty("confirmationSeverity").GetString() == "medium" &&
            capability.GetProperty("responseKind").GetString() == "bulk_operation_result" &&
            capability.GetProperty("resultCollectionProperty").GetString() == "torrentItems" &&
            capability.GetProperty("requiredFields").EnumerateArray().Any(static field => field.GetString() == "infoHash"));
        Assert.Contains(capabilities, static capability =>
            capability.GetProperty("action").GetString() == "admin.deactivate.torrent" &&
            capability.GetProperty("httpMethod").GetString() == "POST" &&
            capability.GetProperty("supportsBulk").GetBoolean() &&
            capability.GetProperty("bulkRoutePattern").GetString() == "/api/admin/torrents/bulk/deactivate");
        Assert.Contains(capabilities, static capability =>
            capability.GetProperty("action").GetString() == "admin.expire.ban" &&
            capability.GetProperty("httpMethod").GetString() == "POST" &&
            capability.GetProperty("supportsBulk").GetBoolean() &&
            capability.GetProperty("bulkRoutePattern").GetString() == "/api/admin/bans/bulk/expire" &&
            capability.GetProperty("selectionMode").GetString() == "multi_select" &&
            capability.GetProperty("idempotencyHint").GetString() == "idempotent");
        Assert.Contains(capabilities, static capability =>
            capability.GetProperty("action").GetString() == "admin.delete.ban" &&
            capability.GetProperty("httpMethod").GetString() == "POST" &&
            capability.GetProperty("supportsBulk").GetBoolean() &&
            capability.GetProperty("bulkRoutePattern").GetString() == "/api/admin/bans/bulk/delete" &&
            capability.GetProperty("selectionMode").GetString() == "multi_select" &&
            capability.GetProperty("idempotencyHint").GetString() == "conditional_idempotent");
        var reauthContext = document.RootElement.GetProperty("reauthenticationContext");
        Assert.Equal("session_missing", reauthContext.GetProperty("reason").GetString());
        Assert.Equal("admin.session.bootstrap", reauthContext.GetProperty("action").GetString());
        Assert.Equal("/", reauthContext.GetProperty("returnUrl").GetString());
        Assert.Equal("/account/login?returnUrl=%2F&reauth=true", reauthContext.GetProperty("reauthenticationUrl").GetString());
        Assert.Equal("low", reauthContext.GetProperty("severity").GetString());
    }

    [Fact]
    public async Task SessionHeartbeat_ReturnsAnonymousShape_WhenNotAuthenticated()
    {
        using var anonymousClient = _adminFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });

        using var response = await anonymousClient.GetAsync("/api/admin/session/heartbeat");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.False(document.RootElement.GetProperty("isAuthenticated").GetBoolean());
        Assert.True(document.RootElement.GetProperty("privilegedSessionFreshUntilUtc").ValueKind is JsonValueKind.Null);
        Assert.False(document.RootElement.GetProperty("requiresPrivilegedReauthentication").GetBoolean());
    }

    [Fact]
    public async Task RootRoute_ServesSpaShell_BeforeAndAfterLogin()
    {
        using var anonymousClient = _adminFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });

        using var anonymousResponse = await anonymousClient.GetAsync("/");
        var anonymousBody = await anonymousResponse.Content.ReadAsStringAsync();
        Assert.Equal(System.Net.HttpStatusCode.OK, anonymousResponse.StatusCode);
        Assert.Contains("<div id=\"root\"></div>", anonymousBody);
        Assert.Contains("BeeTracker Admin", anonymousBody);

        using var browserClient = _adminFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        await LoginWithCookieAsync(browserClient, "ops-admin", "Integration123!");

        using var landingResponse = await browserClient.GetAsync("/");
        var landingBody = await landingResponse.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, landingResponse.StatusCode);
        Assert.Contains("<div id=\"root\"></div>", landingBody);
        Assert.Contains("BeeTracker Admin", landingBody);
    }

    [Fact]
    public async Task AdminUiConfigEndpoint_ReturnsSpaOidcConfiguration()
    {
        using var response = await _adminClient.GetAsync("/admin-ui/config");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(body);
        Assert.Equal("https://localhost", document.RootElement.GetProperty("authority").GetString());
        Assert.Equal("admin-ui", document.RootElement.GetProperty("clientId").GetString());
        Assert.Equal("https://localhost/oidc/callback", document.RootElement.GetProperty("redirectUri").GetString());
        Assert.Contains("openid", document.RootElement.GetProperty("scope").GetString());
        Assert.Contains("admin_api", document.RootElement.GetProperty("scope").GetString());
        Assert.Equal("code", document.RootElement.GetProperty("responseType").GetString());
    }

    [Fact]
    public async Task LegacyTrackerAccessAlias_EmitsDeprecationHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/permissions?page=1&pageSize=10");
        request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", _csrfToken);

        using var response = await _adminClient.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("true", string.Join(",", response.Headers.GetValues("Deprecation")));
        Assert.Equal("Wed, 30 Sep 2026 00:00:00 GMT", string.Join(",", response.Headers.GetValues("Sunset")));
        Assert.Contains("</api/admin/tracker-access>; rel=\"successor-version\"", string.Join(",", response.Headers.GetValues("Link")));
    }

    [Fact]
    public async Task RbacEndpoints_ReturnRoleAndPermissionCatalog_ForSuperAdmin()
    {
        using var superAdminClient = _adminFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        await LoginWithCookieAsync(superAdminClient, "rbac-root", "Integration123!");
        var csrfToken = await GetAdminCsrfTokenAsync(superAdminClient, "/roles");

        using var rolesRequest = new HttpRequestMessage(HttpMethod.Get, "/api/admin/rbac/roles");
        rolesRequest.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrfToken);
        using var permissionsRequest = new HttpRequestMessage(HttpMethod.Get, "/api/admin/rbac/permissions");
        permissionsRequest.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrfToken);
        using var profileRequest = new HttpRequestMessage(HttpMethod.Get, "/api/admin/rbac/profile");
        profileRequest.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrfToken);

        var rolesResponse = await superAdminClient.SendAsync(rolesRequest);
        var permissionsResponse = await superAdminClient.SendAsync(permissionsRequest);
        var profileResponse = await superAdminClient.SendAsync(profileRequest);

        var rolesBody = await rolesResponse.Content.ReadAsStringAsync();
        var permissionsBody = await permissionsResponse.Content.ReadAsStringAsync();
        var profileBody = await profileResponse.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, rolesResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, permissionsResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, profileResponse.StatusCode);
        Assert.Contains("SuperAdmin", rolesBody);
        Assert.Contains("admin.roles.assign_permissions", permissionsBody);
        Assert.Contains("admin.users.assign_roles", profileBody);
    }

    [Fact]
    public async Task RbacEndpoints_BlockSelfDemotion_OfLastActiveSuperAdmin()
    {
        using var superAdminClient = _adminFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        await LoginWithCookieAsync(superAdminClient, "rbac-root", "Integration123!");
        var csrfToken = await GetAdminCsrfTokenAsync(superAdminClient, "/admin-users");

        var profileResponse = await superAdminClient.GetAsync("/api/admin/rbac/profile");
        var profileBody = await profileResponse.Content.ReadAsStringAsync();
        using var profileDocument = JsonDocument.Parse(profileBody);
        var userId = profileDocument.RootElement.GetProperty("userId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(userId));

        using var assignRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/rbac/users/{userId}/roles");
        assignRequest.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrfToken);
        assignRequest.Content = JsonContent.Create(new { roles = Array.Empty<string>() });

        var assignResponse = await superAdminClient.SendAsync(assignRequest);
        var assignBody = await assignResponse.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, assignResponse.StatusCode);
        Assert.Contains("LAST_SUPERADMIN", assignBody);
    }

    [Fact]
    public async Task PermissionProtectedEndpoints_RejectStalePermissionSnapshots()
    {
        using var superAdminClient = _adminFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        await LoginWithCookieAsync(superAdminClient, "rbac-root", "Integration123!");
        _ = await GetAdminCsrfTokenAsync(superAdminClient, "/roles");

        await using (var scope = _adminFactory.Services.CreateAsyncScope())
        {
            var rbacService = scope.ServiceProvider.GetRequiredService<Identity.SelfService.Application.IRbacService>();
            await rbacService.InvalidatePermissionSnapshotAsync(CancellationToken.None);
        }

        var response = await superAdminClient.GetAsync("/api/admin/rbac/roles");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("admin_permission_snapshot_expired", body);
    }

    [Fact]
    public async Task AdminEndpoints_ReturnRealtimeClusterAndHistoricalAuditData()
    {
        await SeedHeartbeatAsync();

        using var upsertRequest = CreateAdminRequest(
            HttpMethod.Put,
            "/api/admin/torrents/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/policy",
            "corr-001");
        upsertRequest.Content = JsonContent.Create(new TorrentPolicyUpsertRequest(
            true,
            true,
            1800,
            900,
            50,
            100,
            true,
            ExpectedVersion: 1));
        var upsertResponse = await _adminClient.SendAsync(upsertRequest);
        Assert.Equal(System.Net.HttpStatusCode.OK, upsertResponse.StatusCode);

        using var maintenanceRequest = CreateAdminRequest(HttpMethod.Post, "/api/admin/maintenance/cache-refresh", "corr-002");
        var maintenanceResponse = await _adminClient.SendAsync(maintenanceRequest);
        Assert.Equal(System.Net.HttpStatusCode.Accepted, maintenanceResponse.StatusCode);

        using var stalePasskeyRequest = CreateAdminRequest(HttpMethod.Put, "/api/admin/passkeys/bootstrap-passkey", "corr-003");
        stalePasskeyRequest.Content = JsonContent.Create(new PasskeyUpsertRequest(
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            true,
            null,
            99));
        var stalePasskeyResponse = await _adminClient.SendAsync(stalePasskeyRequest);
        var stalePasskeyBody = await stalePasskeyResponse.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.Conflict, stalePasskeyResponse.StatusCode);
        Assert.Contains("concurrency_conflict", stalePasskeyBody);

        await WaitUntilAsync(HasAuditRecordsAsync, value => value, TimeSpan.FromSeconds(5));

        var clusterResponse = await _adminClient.SendAsync(CreateAdminRequest(HttpMethod.Get, "/api/admin/cluster-overview", "corr-004"));
        var auditResponse = await _adminClient.SendAsync(CreateAdminRequest(HttpMethod.Get, "/api/admin/audit?page=1&pageSize=20", "corr-005"));
        var maintenanceHistoryResponse = await _adminClient.SendAsync(CreateAdminRequest(HttpMethod.Get, "/api/admin/maintenance?page=1&pageSize=20", "corr-006"));
        var torrentsResponse = await _adminClient.SendAsync(CreateAdminRequest(HttpMethod.Get, "/api/admin/torrents?search=AAAA&page=1&pageSize=20", "corr-007"));
        var torrentDetailResponse = await _adminClient.SendAsync(CreateAdminRequest(HttpMethod.Get, "/api/admin/torrents/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "corr-008"));
        var passkeysResponse = await _adminClient.SendAsync(CreateAdminRequest(HttpMethod.Get, "/api/admin/passkeys?isRevoked=false&page=1&pageSize=20", "corr-009"));
        var permissionsResponse = await _adminClient.SendAsync(CreateAdminRequest(HttpMethod.Get, "/api/admin/tracker-access?canUsePrivateTracker=true&page=1&pageSize=20", "corr-010"));
        var bansResponse = await _adminClient.SendAsync(CreateAdminRequest(HttpMethod.Get, "/api/admin/bans?scope=user&page=1&pageSize=20", "corr-011"));

        var clusterBody = await clusterResponse.Content.ReadAsStringAsync();
        var auditBody = await auditResponse.Content.ReadAsStringAsync();
        var maintenanceBody = await maintenanceHistoryResponse.Content.ReadAsStringAsync();
        var torrentsBody = await torrentsResponse.Content.ReadAsStringAsync();
        var torrentDetailBody = await torrentDetailResponse.Content.ReadAsStringAsync();
        var passkeysBody = await passkeysResponse.Content.ReadAsStringAsync();
        var permissionsBody = await permissionsResponse.Content.ReadAsStringAsync();
        var bansBody = await bansResponse.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, clusterResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, auditResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, maintenanceHistoryResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, torrentsResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, torrentDetailResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, passkeysResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, permissionsResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, bansResponse.StatusCode);
        Assert.Contains("admin-node-1", clusterBody);
        Assert.Contains("ops-admin", auditBody);
        Assert.Contains("security-admin", auditBody);
        Assert.Contains("torrent_policy.upsert", auditBody);
        Assert.Contains("\"severity\":\"high\"", auditBody);
        Assert.Contains("cache-refresh", maintenanceBody);
        Assert.Contains("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", torrentsBody);
        Assert.Contains("\"allowScrape\":true", torrentDetailBody);
        Assert.Contains("\"version\":2", torrentDetailBody);
        Assert.Contains("pk:boot", passkeysBody);
        Assert.Contains("\"version\":1", passkeysBody);
        Assert.Contains("\"canUsePrivateTracker\":true", permissionsBody);
        Assert.Contains("bootstrap test ban", bansBody);
    }

    [Fact]
    public async Task AuthorizationCodeFlow_IssuesAccessToken_WithPkce()
    {
        using var browserClient = _adminFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        const string state = "state-123";
        var codeVerifier = "verifier-1234567890-verifier-1234567890";
        var codeChallenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

        const string redirectUri = "https://app.example.test/signin-oidc";
        var authorizeUri =
            $"/connect/authorize?client_id=admin-ui&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=code&scope={AdminApiScope}&state={state}&code_challenge={codeChallenge}&code_challenge_method=S256";

        var authorizeResponse = await browserClient.GetAsync(authorizeUri);
        Assert.Equal(System.Net.HttpStatusCode.Redirect, authorizeResponse.StatusCode);
        Assert.NotNull(authorizeResponse.Headers.Location);
        Assert.Contains("/account/login", authorizeResponse.Headers.Location!.OriginalString, StringComparison.Ordinal);

        var returnUrl = GetQueryParameter(authorizeResponse.Headers.Location, "ReturnUrl")
            ?? GetQueryParameter(authorizeResponse.Headers.Location, "returnUrl");
        Assert.False(string.IsNullOrWhiteSpace(returnUrl));

        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/account/login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string?>
            {
                ["username"] = "ops-admin",
                ["password"] = "Integration123!",
                ["returnUrl"] = returnUrl
            }!)
        };

        var loginResponse = await browserClient.SendAsync(loginRequest);
        var loginBody = await loginResponse.Content.ReadAsStringAsync();
        Assert.True(loginResponse.StatusCode == System.Net.HttpStatusCode.Redirect, loginBody);
        Assert.NotNull(loginResponse.Headers.Location);

        var consentResponse = await browserClient.GetAsync(loginResponse.Headers.Location);
        Assert.Equal(System.Net.HttpStatusCode.Redirect, consentResponse.StatusCode);
        Assert.NotNull(consentResponse.Headers.Location);
        Assert.StartsWith(redirectUri, consentResponse.Headers.Location!.OriginalString, StringComparison.Ordinal);

        var authorizationCode = GetQueryParameter(consentResponse.Headers.Location, "code");
        var returnedState = GetQueryParameter(consentResponse.Headers.Location, "state");

        Assert.False(string.IsNullOrWhiteSpace(authorizationCode));
        Assert.Equal(state, returnedState);

        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "/connect/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string?>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = "admin-ui",
                ["redirect_uri"] = redirectUri,
                ["code"] = authorizationCode,
                ["code_verifier"] = codeVerifier
            }!)
        };

        var tokenResponse = await browserClient.SendAsync(tokenRequest);
        var tokenBody = await tokenResponse.Content.ReadAsStringAsync();

        Assert.True(tokenResponse.IsSuccessStatusCode, tokenBody);
        using var tokenDocument = JsonDocument.Parse(tokenBody);
        Assert.True(tokenDocument.RootElement.TryGetProperty("access_token", out var accessTokenElement));
        Assert.False(string.IsNullOrWhiteSpace(accessTokenElement.GetString()));
        Assert.Equal("Bearer", tokenDocument.RootElement.GetProperty("token_type").GetString());
    }

    [Fact]
    public async Task PasswordGrant_ReturnsUnsupportedGrant_WhenDisabled()
    {
        await using var disabledFactory = new AdminApiFactory(_postgresConnectionString, _redisConnectionString, allowPasswordGrant: false);
        using var client = disabledFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string?>
            {
                ["grant_type"] = "password",
                ["username"] = "ops-admin",
                ["password"] = "Integration123!"
            }!)
        };

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("unsupported_grant_type", body);
    }

    [Fact]
    public async Task AdminMutation_ReturnsBadRequest_WhenCsrfTokenIsMissing()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/maintenance/cache-refresh");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", "corr-no-csrf");

        var response = await _adminClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid_csrf_token", body);
    }

    [Fact]
    public async Task BulkAdminMutations_ReturnPartialSuccess_WhenSomeItemsHaveConcurrencyConflicts()
    {
        using var permissionsRequest = CreateAdminRequest(HttpMethod.Put, "/api/admin/users/bulk/tracker-access", "corr-bulk-permissions");
        permissionsRequest.Content = JsonContent.Create(new
        {
            items = new object[]
            {
                new
                {
                    userId = "00000000-0000-0000-0000-000000000002",
                    canLeech = true,
                    canSeed = false,
                    canScrape = true,
                    canUsePrivateTracker = true,
                    expectedVersion = 1
                },
                new
                {
                    userId = "00000000-0000-0000-0000-000000000099",
                    canLeech = true,
                    canSeed = true,
                    canScrape = true,
                    canUsePrivateTracker = false,
                    expectedVersion = 99
                }
            }
        });

        var permissionsResponse = await _adminClient.SendAsync(permissionsRequest);
        var permissionsBody = await permissionsResponse.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, permissionsResponse.StatusCode);
        using (var document = JsonDocument.Parse(permissionsBody))
        {
            Assert.Equal(2, document.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(1, document.RootElement.GetProperty("succeededCount").GetInt32());
            Assert.Equal(1, document.RootElement.GetProperty("failedCount").GetInt32());
            var trackerAccessItems = document.RootElement.GetProperty("trackerAccessItems").EnumerateArray().ToArray();
            Assert.Equal(2, trackerAccessItems.Length);
            Assert.Contains(trackerAccessItems, static item =>
                item.GetProperty("userId").GetGuid() == Guid.Parse("00000000-0000-0000-0000-000000000002") &&
                item.GetProperty("succeeded").GetBoolean() &&
                item.GetProperty("snapshot").GetProperty("version").GetInt64() == 2);
            Assert.Contains(trackerAccessItems, static item =>
                item.GetProperty("userId").GetGuid() == Guid.Parse("00000000-0000-0000-0000-000000000099") &&
                item.GetProperty("succeeded").GetBoolean() is false &&
                item.GetProperty("errorCode").GetString() == "concurrency_conflict");
        }

        using var bansRequest = CreateAdminRequest(HttpMethod.Put, "/api/admin/bans/bulk", "corr-bulk-bans");
        bansRequest.Content = JsonContent.Create(new
        {
            items = new object[]
            {
                new
                {
                    scope = "user",
                    subject = "00000000-0000-0000-0000-000000000003",
                    reason = "bulk inserted ban",
                    expiresAtUtc = (DateTimeOffset?)null,
                    expectedVersion = (long?)null
                },
                new
                {
                    scope = "user",
                    subject = "00000000-0000-0000-0000-000000000002",
                    reason = "stale ban update",
                    expiresAtUtc = (DateTimeOffset?)null,
                    expectedVersion = 99
                }
            }
        });

        var bansResponse = await _adminClient.SendAsync(bansRequest);
        var bansBody = await bansResponse.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, bansResponse.StatusCode);
        using (var document = JsonDocument.Parse(bansBody))
        {
            Assert.Equal(2, document.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(1, document.RootElement.GetProperty("succeededCount").GetInt32());
            Assert.Equal(1, document.RootElement.GetProperty("failedCount").GetInt32());
            var banItems = document.RootElement.GetProperty("banItems").EnumerateArray().ToArray();
            Assert.Equal(2, banItems.Length);
            Assert.Contains(banItems, static item =>
                item.GetProperty("scope").GetString() == "user" &&
                item.GetProperty("subject").GetString() == "00000000-0000-0000-0000-000000000003" &&
                item.GetProperty("succeeded").GetBoolean() &&
                item.GetProperty("snapshot").GetProperty("reason").GetString() == "bulk inserted ban");
            Assert.Contains(banItems, static item =>
                item.GetProperty("scope").GetString() == "user" &&
                item.GetProperty("subject").GetString() == "00000000-0000-0000-0000-000000000002" &&
                item.GetProperty("succeeded").GetBoolean() is false &&
                item.GetProperty("errorCode").GetString() == "concurrency_conflict");
        }
    }

    [Fact]
    public async Task BulkBanLifecycleEndpoints_ReturnPartialSuccess_WhenSomeItemsAreMissing()
    {
        using var expireRequest = CreateAdminRequest(HttpMethod.Post, "/api/admin/bans/bulk/expire", "corr-bulk-ban-expire");
        expireRequest.Content = JsonContent.Create(new
        {
            items = new object[]
            {
                new
                {
                    scope = "user",
                    subject = "00000000-0000-0000-0000-000000000002",
                    expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
                    expectedVersion = 1
                },
                new
                {
                    scope = "user",
                    subject = "00000000-0000-0000-0000-000000000099",
                    expiresAtUtc = DateTimeOffset.UtcNow.AddHours(2),
                    expectedVersion = (long?)null
                }
            }
        });

        var expireResponse = await _adminClient.SendAsync(expireRequest);
        var expireBody = await expireResponse.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, expireResponse.StatusCode);
        using (var document = JsonDocument.Parse(expireBody))
        {
            Assert.Equal(2, document.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(1, document.RootElement.GetProperty("succeededCount").GetInt32());
            Assert.Equal(1, document.RootElement.GetProperty("failedCount").GetInt32());
            var banItems = document.RootElement.GetProperty("banItems").EnumerateArray().ToArray();
            Assert.Contains(banItems, static item =>
                item.GetProperty("scope").GetString() == "user" &&
                item.GetProperty("subject").GetString() == "00000000-0000-0000-0000-000000000002" &&
                item.GetProperty("succeeded").GetBoolean() &&
                item.GetProperty("snapshot").GetProperty("version").GetInt64() == 2);
            Assert.Contains(banItems, static item =>
                item.GetProperty("scope").GetString() == "user" &&
                item.GetProperty("subject").GetString() == "00000000-0000-0000-0000-000000000099" &&
                item.GetProperty("succeeded").GetBoolean() is false &&
                item.GetProperty("errorCode").GetString() == "not_found");
        }

        using var deleteRequest = CreateAdminRequest(HttpMethod.Post, "/api/admin/bans/bulk/delete", "corr-bulk-ban-delete");
        deleteRequest.Content = JsonContent.Create(new
        {
            items = new object[]
            {
                new
                {
                    scope = "user",
                    subject = "00000000-0000-0000-0000-000000000002",
                    expectedVersion = 2
                },
                new
                {
                    scope = "user",
                    subject = "00000000-0000-0000-0000-000000000099",
                    expectedVersion = (long?)null
                }
            }
        });

        var deleteResponse = await _adminClient.SendAsync(deleteRequest);
        var deleteBody = await deleteResponse.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, deleteResponse.StatusCode);
        using (var document = JsonDocument.Parse(deleteBody))
        {
            Assert.Equal(2, document.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(1, document.RootElement.GetProperty("succeededCount").GetInt32());
            Assert.Equal(1, document.RootElement.GetProperty("failedCount").GetInt32());
            var banItems = document.RootElement.GetProperty("banItems").EnumerateArray().ToArray();
            Assert.Contains(banItems, static item =>
                item.GetProperty("scope").GetString() == "user" &&
                item.GetProperty("subject").GetString() == "00000000-0000-0000-0000-000000000002" &&
                item.GetProperty("succeeded").GetBoolean() &&
                item.GetProperty("snapshot").ValueKind is JsonValueKind.Null);
            Assert.Contains(banItems, static item =>
                item.GetProperty("scope").GetString() == "user" &&
                item.GetProperty("subject").GetString() == "00000000-0000-0000-0000-000000000099" &&
                item.GetProperty("succeeded").GetBoolean() is false &&
                item.GetProperty("errorCode").GetString() == "not_found");
        }
    }

    [Fact]
    public async Task BulkPasskeyLifecycleEndpoints_ReturnPartialSuccess_AndExposeNewPasskeyOnlyInMutationResponse()
    {
        using var revokeRequest = CreateAdminRequest(HttpMethod.Post, "/api/admin/passkeys/bulk/revoke", "corr-bulk-passkey-revoke");
        revokeRequest.Content = JsonContent.Create(new
        {
            items = new object[]
            {
                new
                {
                    passkey = "bootstrap-passkey",
                    expectedVersion = 1
                },
                new
                {
                    passkey = "missing-passkey",
                    expectedVersion = (long?)null
                }
            }
        });

        var revokeResponse = await _adminClient.SendAsync(revokeRequest);
        var revokeBody = await revokeResponse.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, revokeResponse.StatusCode);
        using (var document = JsonDocument.Parse(revokeBody))
        {
            Assert.Equal(2, document.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(1, document.RootElement.GetProperty("succeededCount").GetInt32());
            Assert.Equal(1, document.RootElement.GetProperty("failedCount").GetInt32());
            var passkeyItems = document.RootElement.GetProperty("passkeyItems").EnumerateArray().ToArray();
            Assert.Contains(passkeyItems, static item =>
                item.GetProperty("passkeyMask").GetString() == "pk:boot...ey" &&
                item.GetProperty("succeeded").GetBoolean() &&
                item.GetProperty("snapshot").GetProperty("isRevoked").GetBoolean() &&
                item.GetProperty("snapshot").GetProperty("version").GetInt64() == 2 &&
                item.GetProperty("newPasskey").ValueKind is JsonValueKind.Null);
            Assert.Contains(passkeyItems, static item =>
                item.GetProperty("passkeyMask").GetString() == "pk:miss...ey" &&
                item.GetProperty("succeeded").GetBoolean() is false &&
                item.GetProperty("errorCode").GetString() == "not_found");
        }

        using var seedRotateSourceRequest = CreateAdminRequest(HttpMethod.Put, "/api/admin/passkeys/rotate-source-passkey", "corr-seed-rotate-source");
        seedRotateSourceRequest.Content = JsonContent.Create(new PasskeyUpsertRequest(
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            false,
            null,
            null));
        var seedRotateSourceResponse = await _adminClient.SendAsync(seedRotateSourceRequest);
        Assert.Equal(System.Net.HttpStatusCode.OK, seedRotateSourceResponse.StatusCode);

        using var rotateRequest = CreateAdminRequest(HttpMethod.Post, "/api/admin/passkeys/bulk/rotate", "corr-bulk-passkey-rotate");
        rotateRequest.Content = JsonContent.Create(new
        {
            items = new object[]
            {
                new
                {
                    passkey = "rotate-source-passkey",
                    expectedVersion = 1
                },
                new
                {
                    passkey = "missing-passkey",
                    expectedVersion = (long?)null
                }
            }
        });

        var rotateResponse = await _adminClient.SendAsync(rotateRequest);
        var rotateBody = await rotateResponse.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, rotateResponse.StatusCode);
        using (var document = JsonDocument.Parse(rotateBody))
        {
            Assert.Equal(2, document.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(1, document.RootElement.GetProperty("succeededCount").GetInt32());
            Assert.Equal(1, document.RootElement.GetProperty("failedCount").GetInt32());
            var passkeyItems = document.RootElement.GetProperty("passkeyItems").EnumerateArray().ToArray();
            Assert.Contains(passkeyItems, static item =>
                item.GetProperty("passkeyMask").GetString() == "pk:rota...ey" &&
                item.GetProperty("succeeded").GetBoolean() &&
                item.GetProperty("snapshot").GetProperty("isRevoked").GetBoolean() &&
                item.GetProperty("snapshot").GetProperty("version").GetInt64() == 2 &&
                item.GetProperty("newPasskey").GetString() is { Length: > 0 } &&
                item.GetProperty("newPasskeyMask").GetString() is { Length: > 0 });
            Assert.Contains(passkeyItems, static item =>
                item.GetProperty("passkeyMask").GetString() == "pk:miss...ey" &&
                item.GetProperty("succeeded").GetBoolean() is false &&
                item.GetProperty("errorCode").GetString() == "not_found");
        }

        using var passkeysResponse = await _adminClient.SendAsync(CreateAdminRequest(HttpMethod.Get, "/api/admin/passkeys?userId=00000000-0000-0000-0000-000000000002&page=1&pageSize=20", "corr-passkeys-after-rotate"));
        var passkeysBody = await passkeysResponse.Content.ReadAsStringAsync();
        Assert.Equal(System.Net.HttpStatusCode.OK, passkeysResponse.StatusCode);
        Assert.DoesNotContain("\"newPasskey\":", passkeysBody);
    }

    [Fact]
    public async Task BulkTorrentLifecycleEndpoints_ReturnPartialSuccess_WhenSomeItemsAreMissing()
    {
        using var deactivateRequest = CreateAdminRequest(HttpMethod.Post, "/api/admin/torrents/bulk/deactivate", "corr-bulk-torrent-deactivate");
        deactivateRequest.Content = JsonContent.Create(new
        {
            items = new object[]
            {
                new
                {
                    infoHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                    expectedVersion = 1
                },
                new
                {
                    infoHash = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
                    expectedVersion = (long?)null
                }
            }
        });

        var deactivateResponse = await _adminClient.SendAsync(deactivateRequest);
        var deactivateBody = await deactivateResponse.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, deactivateResponse.StatusCode);
        using (var document = JsonDocument.Parse(deactivateBody))
        {
            Assert.Equal(2, document.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(1, document.RootElement.GetProperty("succeededCount").GetInt32());
            Assert.Equal(1, document.RootElement.GetProperty("failedCount").GetInt32());
            var torrentItems = document.RootElement.GetProperty("torrentItems").EnumerateArray().ToArray();
            Assert.Contains(torrentItems, static item =>
                item.GetProperty("infoHash").GetString() == "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" &&
                item.GetProperty("succeeded").GetBoolean() &&
                item.GetProperty("snapshot").GetProperty("isEnabled").GetBoolean() is false);
            Assert.Contains(torrentItems, static item =>
                item.GetProperty("infoHash").GetString() == "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB" &&
                item.GetProperty("succeeded").GetBoolean() is false &&
                item.GetProperty("errorCode").GetString() == "not_found");
        }

        using var activateRequest = CreateAdminRequest(HttpMethod.Post, "/api/admin/torrents/bulk/activate", "corr-bulk-torrent-activate");
        activateRequest.Content = JsonContent.Create(new
        {
            items = new object[]
            {
                new
                {
                    infoHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                    expectedVersion = 2
                },
                new
                {
                    infoHash = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
                    expectedVersion = (long?)null
                }
            }
        });

        var activateResponse = await _adminClient.SendAsync(activateRequest);
        var activateBody = await activateResponse.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, activateResponse.StatusCode);
        using (var document = JsonDocument.Parse(activateBody))
        {
            Assert.Equal(2, document.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(1, document.RootElement.GetProperty("succeededCount").GetInt32());
            Assert.Equal(1, document.RootElement.GetProperty("failedCount").GetInt32());
            var torrentItems = document.RootElement.GetProperty("torrentItems").EnumerateArray().ToArray();
            Assert.Contains(torrentItems, static item =>
                item.GetProperty("infoHash").GetString() == "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" &&
                item.GetProperty("succeeded").GetBoolean() &&
                item.GetProperty("snapshot").GetProperty("isEnabled").GetBoolean());
            Assert.Contains(torrentItems, static item =>
                item.GetProperty("infoHash").GetString() == "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB" &&
                item.GetProperty("succeeded").GetBoolean() is false &&
                item.GetProperty("errorCode").GetString() == "not_found");
        }
    }

    [Fact]
    public async Task BulkTorrentPolicyEndpoint_ReturnsPartialSuccess_WhenSomeItemsHaveConcurrencyConflicts()
    {
        using var request = CreateAdminRequest(HttpMethod.Put, "/api/admin/torrents/bulk/policy", "corr-bulk-torrent-policy");
        request.Content = JsonContent.Create(new
        {
            items = new object[]
            {
                new
                {
                    infoHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                    isPrivate = true,
                    isEnabled = true,
                    announceIntervalSeconds = 1200,
                    minAnnounceIntervalSeconds = 600,
                    defaultNumWant = 60,
                    maxNumWant = 120,
                    allowScrape = false,
                    expectedVersion = 1
                },
                new
                {
                    infoHash = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC",
                    isPrivate = false,
                    isEnabled = true,
                    announceIntervalSeconds = 1800,
                    minAnnounceIntervalSeconds = 900,
                    defaultNumWant = 50,
                    maxNumWant = 100,
                    allowScrape = true,
                    expectedVersion = 99
                }
            }
        });

        var response = await _adminClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(2, document.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("succeededCount").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("failedCount").GetInt32());
        var torrentItems = document.RootElement.GetProperty("torrentItems").EnumerateArray().ToArray();
        Assert.Contains(torrentItems, static item =>
            item.GetProperty("infoHash").GetString() == "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" &&
            item.GetProperty("succeeded").GetBoolean() &&
            item.GetProperty("snapshot").GetProperty("announceIntervalSeconds").GetInt32() == 1200 &&
            item.GetProperty("snapshot").GetProperty("allowScrape").GetBoolean() is false &&
            item.GetProperty("snapshot").GetProperty("version").GetInt64() == 2);
        Assert.Contains(torrentItems, static item =>
            item.GetProperty("infoHash").GetString() == "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC" &&
            item.GetProperty("succeeded").GetBoolean() is false &&
            item.GetProperty("errorCode").GetString() == "concurrency_conflict");
    }

    [Fact]
    public async Task BulkTorrentPolicyDryRunEndpoint_ReturnsPreviewWithoutPersistingChanges()
    {
        using var request = CreateAdminRequest(HttpMethod.Post, "/api/admin/torrents/bulk/policy/dry-run", "corr-bulk-torrent-policy-dry-run");
        request.Content = JsonContent.Create(new
        {
            items = new object[]
            {
                new
                {
                    infoHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                    isPrivate = true,
                    isEnabled = false,
                    announceIntervalSeconds = 900,
                    minAnnounceIntervalSeconds = 450,
                    defaultNumWant = 40,
                    maxNumWant = 80,
                    allowScrape = false,
                    expectedVersion = 1
                },
                new
                {
                    infoHash = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC",
                    isPrivate = false,
                    isEnabled = true,
                    announceIntervalSeconds = 1800,
                    minAnnounceIntervalSeconds = 900,
                    defaultNumWant = 50,
                    maxNumWant = 100,
                    allowScrape = true,
                    expectedVersion = 99
                },
                new
                {
                    infoHash = "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD",
                    isPrivate = false,
                    isEnabled = true,
                    announceIntervalSeconds = 1500,
                    minAnnounceIntervalSeconds = 750,
                    defaultNumWant = 55,
                    maxNumWant = 110,
                    allowScrape = true,
                    expectedVersion = (long?)null
                }
            }
        });

        var response = await _adminClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using (var document = JsonDocument.Parse(body))
        {
            Assert.Equal(3, document.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(2, document.RootElement.GetProperty("applicableCount").GetInt32());
            Assert.Equal(1, document.RootElement.GetProperty("rejectedCount").GetInt32());
            var items = document.RootElement.GetProperty("torrentPolicyItems").EnumerateArray().ToArray();
            Assert.Contains(items, static item =>
                item.GetProperty("infoHash").GetString() == "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" &&
                item.GetProperty("canApply").GetBoolean() &&
                item.GetProperty("warnings").EnumerateArray().Any() is false &&
                item.GetProperty("currentSnapshot").GetProperty("version").GetInt64() == 1 &&
                item.GetProperty("proposedSnapshot").GetProperty("version").GetInt64() == 2 &&
                item.GetProperty("proposedSnapshot").GetProperty("announceIntervalSeconds").GetInt32() == 900 &&
                item.GetProperty("proposedSnapshot").GetProperty("isEnabled").GetBoolean() is false);
            Assert.Contains(items, static item =>
                item.GetProperty("infoHash").GetString() == "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC" &&
                item.GetProperty("canApply").GetBoolean() is false &&
                item.GetProperty("errorCode").GetString() == "concurrency_conflict" &&
                item.GetProperty("currentSnapshot").ValueKind is JsonValueKind.Null &&
                item.GetProperty("proposedSnapshot").GetProperty("version").GetInt64() == 1);
            Assert.Contains(items, static item =>
                item.GetProperty("infoHash").GetString() == "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD" &&
                item.GetProperty("canApply").GetBoolean() &&
                item.GetProperty("currentSnapshot").ValueKind is JsonValueKind.Null &&
                item.GetProperty("warnings").EnumerateArray().Any(static warning => warning.GetString() == "This change will create a new torrent policy row for an existing torrent that currently has no explicit policy row."));
        }

        var detailResponse = await _adminClient.SendAsync(CreateAdminRequest(HttpMethod.Get, "/api/admin/torrents/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "corr-bulk-torrent-policy-dry-run-detail"));
        var detailBody = await detailResponse.Content.ReadAsStringAsync();
        Assert.Equal(System.Net.HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.Contains("\"announceIntervalSeconds\":1800", detailBody);
        Assert.Contains("\"allowScrape\":true", detailBody);
    }

    [Fact]
    public async Task PrivilegedMutation_ReturnsReauthenticationRequired_WhenSessionIsStale()
    {
        var fakeTimeProvider = new MutableTimeProvider(DateTimeOffset.UtcNow);
        await using var staleFactory = new AdminApiFactory(
            _postgresConnectionString,
            _redisConnectionString,
            allowPasswordGrant: false,
            timeProvider: fakeTimeProvider,
            privilegedReauthenticationMinutes: 1);
        using var staleClient = staleFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        await LoginWithCookieAsync(staleClient, "ops-admin", "Integration123!");
        var csrfToken = await GetAdminCsrfTokenAsync(staleClient);
        fakeTimeProvider.Advance(TimeSpan.FromMinutes(2));

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/admin/passkeys/bootstrap-passkey?returnUrl=%2Fadmin%2Fpasskeys%2Fbootstrap-passkey");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", "corr-reauth");
        request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrfToken);
        request.Content = JsonContent.Create(new PasskeyUpsertRequest(
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            false,
            null,
            1));

        var response = await staleClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("admin_reauthentication_required", document.RootElement.GetProperty("code").GetString());
        Assert.Equal(
            "/account/login?returnUrl=%2Fadmin%2Fpasskeys%2Fbootstrap-passkey&reauth=true",
            document.RootElement.GetProperty("reauthenticationUrl").GetString());
        var reauthContext = document.RootElement.GetProperty("reauthenticationContext");
        Assert.Equal("session_stale", reauthContext.GetProperty("reason").GetString());
        Assert.Equal("PUT /api/admin/passkeys/bootstrap-passkey", reauthContext.GetProperty("action").GetString());
        Assert.Equal("/admin/passkeys/bootstrap-passkey", reauthContext.GetProperty("returnUrl").GetString());
        Assert.Equal("high", reauthContext.GetProperty("severity").GetString());
    }

    [Fact]
    public async Task SessionHeartbeat_ReturnsFreshnessState_ForAuthenticatedSession()
    {
        using var response = await _adminClient.SendAsync(CreateAdminRequest(HttpMethod.Get, "/api/admin/session/heartbeat", "corr-heartbeat"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.True(document.RootElement.GetProperty("isAuthenticated").GetBoolean());
        Assert.True(document.RootElement.TryGetProperty("privilegedSessionFreshUntilUtc", out _));
        Assert.False(document.RootElement.GetProperty("requiresPrivilegedReauthentication").GetBoolean());
    }

    [Fact]
    public async Task SessionCapabilities_ReturnGrantedAndPrivilegedMetadata_ForAuthenticatedSession()
    {
        using var response = await _adminClient.SendAsync(CreateAdminRequest(HttpMethod.Get, "/api/admin/session/capabilities", "corr-capabilities"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var capabilities = document.RootElement.EnumerateArray().ToArray();

        Assert.Contains(capabilities, static capability =>
            capability.GetProperty("action").GetString() == "admin.read.cluster_overview" &&
            capability.GetProperty("permission").GetString() == "admin.dashboard.view" &&
            capability.GetProperty("httpMethod").GetString() == "GET" &&
            capability.GetProperty("supportsBulk").GetBoolean() is false &&
            capability.GetProperty("bulkRoutePattern").ValueKind is JsonValueKind.Null &&
            capability.GetProperty("selectionMode").GetString() == "none" &&
            capability.GetProperty("idempotencyHint").GetString() == "safe" &&
            capability.GetProperty("confirmationRequired").GetBoolean() is false &&
            capability.GetProperty("granted").GetBoolean() &&
            capability.GetProperty("requiresPrivilegedReauthentication").GetBoolean() is false &&
            capability.GetProperty("severity").GetString() == "low" &&
            capability.GetProperty("category").GetString() == "monitoring" &&
            capability.GetProperty("resourceKind").GetString() == "cluster" &&
            capability.GetProperty("routePattern").GetString() == "/api/admin/cluster-overview" &&
            capability.GetProperty("displayName").GetString() == "View cluster overview");

        Assert.Contains(capabilities, static capability =>
            capability.GetProperty("action").GetString() == "admin.write.passkey" &&
            capability.GetProperty("permission").GetString() == "admin.passkeys.manage" &&
            capability.GetProperty("httpMethod").GetString() == "PUT" &&
            capability.GetProperty("supportsBulk").GetBoolean() is false &&
            capability.GetProperty("bulkRoutePattern").ValueKind is JsonValueKind.Null &&
            capability.GetProperty("selectionMode").GetString() == "single" &&
            capability.GetProperty("idempotencyHint").GetString() == "idempotent" &&
            capability.GetProperty("confirmationRequired").GetBoolean() &&
            capability.GetProperty("granted").GetBoolean() &&
            capability.GetProperty("requiresPrivilegedReauthentication").GetBoolean() &&
            capability.GetProperty("severity").GetString() == "high" &&
            capability.GetProperty("category").GetString() == "access" &&
            capability.GetProperty("resourceKind").GetString() == "passkey" &&
            capability.GetProperty("routePattern").GetString() == "/api/admin/passkeys/{passkey}" &&
            capability.GetProperty("displayName").GetString() == "Manage passkeys" &&
            capability.GetProperty("reauthenticationPrompt").GetString() == "Sign in again to confirm this sensitive passkey change.");

        Assert.Contains(capabilities, static capability =>
            capability.GetProperty("action").GetString() == "admin.write.permissions" &&
            capability.GetProperty("permission").GetString() == "admin.tracker_access.manage" &&
            capability.GetProperty("httpMethod").GetString() == "PUT" &&
            capability.GetProperty("supportsBulk").GetBoolean() &&
            capability.GetProperty("bulkRoutePattern").GetString() == "/api/admin/users/bulk/tracker-access" &&
            capability.GetProperty("selectionMode").GetString() == "multi_select" &&
            capability.GetProperty("idempotencyHint").GetString() == "idempotent" &&
            capability.GetProperty("confirmationRequired").GetBoolean() &&
            capability.GetProperty("granted").GetBoolean() &&
            capability.GetProperty("requiresPrivilegedReauthentication").GetBoolean() &&
            capability.GetProperty("severity").GetString() == "high" &&
            capability.GetProperty("resourceKind").GetString() == "tracker-access" &&
            capability.GetProperty("routePattern").GetString() == "/api/admin/users/{userId}/tracker-access" &&
            capability.GetProperty("resultCollectionProperty").GetString() == "trackerAccessItems");

        Assert.Contains(capabilities, static capability =>
            capability.GetProperty("action").GetString() == "admin.execute.maintenance" &&
            capability.GetProperty("permission").GetString() == "admin.maintenance.execute" &&
            capability.GetProperty("httpMethod").GetString() == "POST" &&
            capability.GetProperty("supportsBulk").GetBoolean() is false &&
            capability.GetProperty("bulkRoutePattern").ValueKind is JsonValueKind.Null &&
            capability.GetProperty("selectionMode").GetString() == "none" &&
            capability.GetProperty("idempotencyHint").GetString() == "non_idempotent" &&
            capability.GetProperty("confirmationRequired").GetBoolean() &&
            capability.GetProperty("granted").GetBoolean() &&
            capability.GetProperty("requiresPrivilegedReauthentication").GetBoolean() &&
            capability.GetProperty("severity").GetString() == "high");
    }

    [Fact]
    public async Task ReauthenticationFlow_RenewsPrivilegedSession_AndAllowsMutation()
    {
        var fakeTimeProvider = new MutableTimeProvider(DateTimeOffset.UtcNow);
        await using var reauthFactory = new AdminApiFactory(
            _postgresConnectionString,
            _redisConnectionString,
            allowPasswordGrant: false,
            timeProvider: fakeTimeProvider,
            privilegedReauthenticationMinutes: 1);
        using var reauthClient = reauthFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        await LoginWithCookieAsync(reauthClient, "ops-admin", "Integration123!");
        var csrfToken = await GetAdminCsrfTokenAsync(reauthClient, "/admin/passkeys/bootstrap-passkey");
        fakeTimeProvider.Advance(TimeSpan.FromMinutes(2));

        using var staleRequest = new HttpRequestMessage(HttpMethod.Put, "/api/admin/passkeys/bootstrap-passkey?returnUrl=%2Fadmin%2Fpasskeys%2Fbootstrap-passkey");
        staleRequest.Headers.TryAddWithoutValidation("X-Correlation-Id", "corr-reauth-stale");
        staleRequest.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrfToken);
        staleRequest.Content = JsonContent.Create(new PasskeyUpsertRequest(
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            false,
            null,
            1));

        var staleResponse = await reauthClient.SendAsync(staleRequest);
        var staleBody = await staleResponse.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, staleResponse.StatusCode);
        using var staleDocument = JsonDocument.Parse(staleBody);
        Assert.Equal("admin_reauthentication_required", staleDocument.RootElement.GetProperty("code").GetString());
        Assert.Equal(
            "/account/login?returnUrl=%2Fadmin%2Fpasskeys%2Fbootstrap-passkey&reauth=true",
            staleDocument.RootElement.GetProperty("reauthenticationUrl").GetString());
        var staleReauthContext = staleDocument.RootElement.GetProperty("reauthenticationContext");
        Assert.Equal("session_stale", staleReauthContext.GetProperty("reason").GetString());
        Assert.Equal("PUT /api/admin/passkeys/bootstrap-passkey", staleReauthContext.GetProperty("action").GetString());
        Assert.Equal("/admin/passkeys/bootstrap-passkey", staleReauthContext.GetProperty("returnUrl").GetString());
        Assert.Equal("high", staleReauthContext.GetProperty("severity").GetString());

        await LoginWithCookieAsync(
            reauthClient,
            "ops-admin",
            "Integration123!",
            "/admin/passkeys/bootstrap-passkey",
            reauth: true);

        var renewedCsrfToken = await GetAdminCsrfTokenAsync(reauthClient, "/admin/passkeys/bootstrap-passkey");
        using var renewedRequest = new HttpRequestMessage(HttpMethod.Put, "/api/admin/passkeys/bootstrap-passkey?returnUrl=%2Fadmin%2Fpasskeys%2Fbootstrap-passkey");
        renewedRequest.Headers.TryAddWithoutValidation("X-Correlation-Id", "corr-reauth-renewed");
        renewedRequest.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", renewedCsrfToken);
        renewedRequest.Content = JsonContent.Create(new PasskeyUpsertRequest(
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            false,
            null,
            1));

        var renewedResponse = await reauthClient.SendAsync(renewedRequest);
        var renewedBody = await renewedResponse.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, renewedResponse.StatusCode);
        Assert.Contains("\"version\":2", renewedBody);
    }

    [Fact]
    public async Task AdminLogout_ClearsSession_AndSessionEndpointReturnsAnonymousShape()
    {
        using var logoutRequest = CreateAdminRequest(HttpMethod.Post, "/api/admin/session/logout", "corr-logout");
        var logoutResponse = await _adminClient.SendAsync(logoutRequest);

        Assert.Equal(System.Net.HttpStatusCode.NoContent, logoutResponse.StatusCode);

        using var sessionResponse = await _adminClient.SendAsync(CreateAdminRequest(HttpMethod.Get, "/api/admin/session", "corr-post-logout-session"));
        var sessionBody = await sessionResponse.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, sessionResponse.StatusCode);
        using var document = JsonDocument.Parse(sessionBody);
        Assert.False(document.RootElement.GetProperty("isAuthenticated").GetBoolean());
        Assert.Equal(string.Empty, document.RootElement.GetProperty("userName").GetString());
        Assert.Equal(string.Empty, document.RootElement.GetProperty("role").GetString());
        Assert.Empty(document.RootElement.GetProperty("permissions").EnumerateArray());
        Assert.Equal(string.Empty, document.RootElement.GetProperty("csrfToken").GetString());
        Assert.True(document.RootElement.GetProperty("privilegedSessionFreshUntilUtc").ValueKind is JsonValueKind.Null);
        Assert.False(document.RootElement.GetProperty("requiresPrivilegedReauthentication").GetBoolean());
        Assert.Equal("/account/login?returnUrl=%2F&reauth=true", document.RootElement.GetProperty("reauthenticationUrl").GetString());
        var reauthContext = document.RootElement.GetProperty("reauthenticationContext");
        Assert.Equal("session_missing", reauthContext.GetProperty("reason").GetString());
        Assert.Equal("admin.session.bootstrap", reauthContext.GetProperty("action").GetString());
        Assert.Equal("/", reauthContext.GetProperty("returnUrl").GetString());
        Assert.Equal("/account/login?returnUrl=%2F&reauth=true", reauthContext.GetProperty("reauthenticationUrl").GetString());

        using var clusterRequest = CreateAdminRequest(HttpMethod.Get, "/api/admin/cluster-overview", "corr-post-logout-protected");
        var clusterResponse = await _adminClient.SendAsync(clusterRequest);
        var clusterBody = await clusterResponse.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, clusterResponse.StatusCode);
        using var clusterDocument = JsonDocument.Parse(clusterBody);
        Assert.Equal("admin_unauthorized", clusterDocument.RootElement.GetProperty("code").GetString());
        Assert.Equal("/account/login?returnUrl=%2F&reauth=true", clusterDocument.RootElement.GetProperty("reauthenticationUrl").GetString());
        var unauthorizedReauthContext = clusterDocument.RootElement.GetProperty("reauthenticationContext");
        Assert.Equal("session_missing", unauthorizedReauthContext.GetProperty("reason").GetString());
        Assert.Equal("GET /api/admin/cluster-overview", unauthorizedReauthContext.GetProperty("action").GetString());
        Assert.Equal("/", unauthorizedReauthContext.GetProperty("returnUrl").GetString());
        Assert.Equal("low", unauthorizedReauthContext.GetProperty("severity").GetString());
    }

    public async Task InitializeAsync()
    {
        await _postgresContainer.StartAsync();
        await _redisContainer.StartAsync();

        _postgresConnectionString = _postgresContainer.GetConnectionString();
        _redisConnectionString = _redisContainer.GetConnectionString();

        await InitializeDatabaseAsync();

        _adminFactory = new AdminApiFactory(_postgresConnectionString, _redisConnectionString);
        _adminClient = _adminFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        await LoginWithCookieAsync(_adminClient, "ops-admin", "Integration123!");
        _csrfToken = await GetAdminCsrfTokenAsync(_adminClient);
    }

    public async Task DisposeAsync()
    {
        _adminClient.Dispose();
        await _adminFactory.DisposeAsync();
        await _postgresContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }

    private async Task SeedHeartbeatAsync()
    {
        await using var redis = await ConnectionMultiplexer.ConnectAsync(_redisConnectionString);
        var payload = JsonSerializer.Serialize(new
        {
            NodeId = "admin-node-1",
            Region = "integration",
            ObservedAtUtc = DateTimeOffset.UtcNow
        });

        await redis.GetDatabase().StringSetAsync("nodes:heartbeat:admin-node-1", payload, TimeSpan.FromSeconds(30));
    }

    private async Task<bool> HasAuditRecordsAsync()
    {
        await using var connection = new NpgsqlConnection(_postgresConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select count(*) from audit_records";
        return (long)(await command.ExecuteScalarAsync() ?? 0L) >= 2L;
    }

    private async Task InitializeDatabaseAsync()
    {
        var configurationOptions = new DbContextOptionsBuilder<TrackerConfigurationDbContext>()
            .UseNpgsql(_postgresConnectionString)
            .Options;

        await using (var configurationDbContext = new TrackerConfigurationDbContext(configurationOptions))
        {
            await configurationDbContext.Database.MigrateAsync();
        }

        await using var connection = new NpgsqlConnection(_postgresConnectionString);
        await connection.OpenAsync();

        var sql =
            """
            insert into torrents (id, info_hash, is_private, is_enabled)
            values ('00000000-0000-0000-0000-000000000010', 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA', true, true)
            on conflict (info_hash) do nothing;

            insert into torrents (id, info_hash, is_private, is_enabled)
            values ('00000000-0000-0000-0000-000000000011', 'DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD', false, true)
            on conflict (info_hash) do nothing;

            insert into torrent_policies (torrent_id, announce_interval_seconds, min_announce_interval_seconds, default_numwant, max_numwant, allow_scrape, row_version)
            values ('00000000-0000-0000-0000-000000000010', 1800, 900, 50, 100, true, 1)
            on conflict (torrent_id) do nothing;

            insert into passkeys (passkey, user_id, is_revoked, expires_at_utc, row_version)
            values ('bootstrap-passkey', '00000000-0000-0000-0000-000000000002', false, null, 1)
            on conflict (passkey) do nothing;

            insert into permissions (user_id, can_leech, can_seed, can_scrape, can_use_private_tracker, row_version)
            values ('00000000-0000-0000-0000-000000000002', true, true, true, true, 1)
            on conflict (user_id) do nothing;

            insert into bans (scope, subject, reason, expires_at_utc, row_version)
            values ('user', '00000000-0000-0000-0000-000000000002', 'bootstrap test ban', null, 1)
            on conflict (scope, subject) do update
                set reason = excluded.reason,
                    expires_at_utc = excluded.expires_at_utc;
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

    private static string? GetQueryParameter(Uri uri, string key)
    {
        var query = uri.IsAbsoluteUri ? uri.Query : new Uri("https://localhost" + uri).Query;
        var parameters = query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static pair => pair.Split('=', 2))
            .ToDictionary(
                static pair => Uri.UnescapeDataString(pair[0]),
                static pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : string.Empty,
                StringComparer.Ordinal);

        return parameters.TryGetValue(key, out var value) ? value : null;
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private async Task LoginWithCookieAsync(HttpClient browserClient, string userName, string password, string returnUrl = "/", bool reauth = false)
    {
        var loginPageUrl = $"/account/login?returnUrl={Uri.EscapeDataString(returnUrl)}&reauth={(reauth ? "true" : "false")}";
        using var loginPageResponse = await browserClient.GetAsync(loginPageUrl);
        Assert.Equal(System.Net.HttpStatusCode.OK, loginPageResponse.StatusCode);

        var loginPageBody = await loginPageResponse.Content.ReadAsStringAsync();
        if (reauth)
        {
            Assert.Contains("Recent authentication is required", loginPageBody);
        }

        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/account/login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string?>
            {
                ["username"] = userName,
                ["password"] = password,
                ["returnUrl"] = returnUrl,
                ["reauth"] = reauth ? "true" : "false"
            }!)
        };

        var loginResponse = await browserClient.SendAsync(loginRequest);
        var loginBody = await loginResponse.Content.ReadAsStringAsync();
        if (loginResponse.StatusCode != System.Net.HttpStatusCode.Redirect)
        {
            throw new InvalidOperationException($"Cookie login failed. Status: {(int)loginResponse.StatusCode}; Body: {loginBody}");
        }
    }

    private async Task<string> GetAdminCsrfTokenAsync(HttpClient browserClient, string returnUrl = "/")
    {
        using var sessionRequest = CreateAdminRequest(HttpMethod.Get, $"/api/admin/session?returnUrl={Uri.EscapeDataString(returnUrl)}", "corr-session");
        var sessionResponse = await browserClient.SendAsync(sessionRequest);
        var sessionBody = await sessionResponse.Content.ReadAsStringAsync();
        if (!sessionResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Session bootstrap failed. Status: {(int)sessionResponse.StatusCode}; Body: {sessionBody}");
        }

        using var document = JsonDocument.Parse(sessionBody);
        var csrfToken = document.RootElement.GetProperty("csrfToken").GetString();
        var reauthenticationUrl = document.RootElement.GetProperty("reauthenticationUrl").GetString();
        Assert.Equal($"/account/login?returnUrl={Uri.EscapeDataString(returnUrl)}&reauth=true", reauthenticationUrl);
        return !string.IsNullOrWhiteSpace(csrfToken)
            ? csrfToken
            : throw new InvalidOperationException($"CSRF token was not returned by /api/admin/session. Response: {sessionBody}");
    }

    private async Task<string> GetAccessTokenViaAuthorizationCodeAsync(HttpClient hostClient, string clientId, string redirectUri)
    {
        using var browserClient = _adminFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = hostClient.BaseAddress ?? new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var state = Guid.NewGuid().ToString("N");
        var codeVerifier = "integration-verifier-1234567890-integration-verifier";
        var codeChallenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        var authorizeUri =
            $"/connect/authorize?client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=code&scope={AdminApiScope}&state={state}&code_challenge={codeChallenge}&code_challenge_method=S256";

        var authorizeResponse = await browserClient.GetAsync(authorizeUri);
        if (authorizeResponse.StatusCode != System.Net.HttpStatusCode.Redirect || authorizeResponse.Headers.Location is null)
        {
            throw new InvalidOperationException($"Authorize endpoint did not return login redirect. Status: {(int)authorizeResponse.StatusCode}");
        }

        var returnUrl = GetQueryParameter(authorizeResponse.Headers.Location, "ReturnUrl")
            ?? GetQueryParameter(authorizeResponse.Headers.Location, "returnUrl")
            ?? throw new InvalidOperationException("Login redirect did not include a returnUrl.");

        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/account/login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string?>
            {
                ["username"] = "ops-admin",
                ["password"] = "Integration123!",
                ["returnUrl"] = returnUrl
            }!)
        };

        var loginResponse = await browserClient.SendAsync(loginRequest);
        if (loginResponse.StatusCode != System.Net.HttpStatusCode.Redirect || loginResponse.Headers.Location is null)
        {
            var loginBody = await loginResponse.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Login did not complete. Status: {(int)loginResponse.StatusCode}; Body: {loginBody}");
        }

        var consentResponse = await browserClient.GetAsync(loginResponse.Headers.Location);
        if (consentResponse.StatusCode != System.Net.HttpStatusCode.Redirect || consentResponse.Headers.Location is null)
        {
            var consentBody = await consentResponse.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Authorize callback did not issue a code. Status: {(int)consentResponse.StatusCode}; Body: {consentBody}");
        }

        var authorizationCode = GetQueryParameter(consentResponse.Headers.Location, "code")
            ?? throw new InvalidOperationException("Authorization redirect did not include a code.");

        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "/connect/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string?>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = clientId,
                ["redirect_uri"] = redirectUri,
                ["code"] = authorizationCode,
                ["code_verifier"] = codeVerifier
            }!)
        };

        var tokenResponse = await browserClient.SendAsync(tokenRequest);
        var tokenBody = await tokenResponse.Content.ReadAsStringAsync();
        if (!tokenResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Token endpoint returned {(int)tokenResponse.StatusCode}: {tokenBody}");
        }

        using var document = JsonDocument.Parse(tokenBody);
        if (!document.RootElement.TryGetProperty("access_token", out var accessTokenElement))
        {
            throw new InvalidOperationException($"Access token was not returned by the admin authorization_code flow. Response: {tokenBody}");
        }

        var accessToken = accessTokenElement.GetString()
            ?? throw new InvalidOperationException($"Access token was empty in the admin authorization_code flow. Response: {tokenBody}");

        return accessToken;
    }

    private sealed class AdminApiFactory(
        string postgresConnectionString,
        string redisConnectionString,
        bool allowPasswordGrant = false,
        TimeProvider? timeProvider = null,
        int privilegedReauthenticationMinutes = 15) : WebApplicationFactory<AdminProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{PostgresOptions.SectionName}:ConnectionString"] = postgresConnectionString,
                    [$"{RedisOptions.SectionName}:Configuration"] = redisConnectionString,
                    [$"{TrackerPublicEndpointOptions.SectionName}:AllowedHosts:0"] = "localhost",
                    [$"{TrackerNodeOptions.SectionName}:NodeId"] = "admin-integration-node",
                    [$"{TrackerNodeOptions.SectionName}:Region"] = "integration",
                    [$"{AdminIdentityOptions.SectionName}:AdminApiScope"] = AdminApiScope,
                    [$"{AdminIdentityOptions.SectionName}:SpaClientId"] = "admin-ui",
                    [$"{AdminIdentityOptions.SectionName}:SpaRedirectPath"] = "/oidc/callback",
                    [$"{AdminIdentityOptions.SectionName}:SpaPostLogoutPath"] = "/",
                    [$"{AdminIdentityOptions.SectionName}:AccessTokenLifetimeMinutes"] = "30",
                    [$"{AdminIdentityOptions.SectionName}:SessionIdleTimeoutMinutes"] = "60",
                    [$"{AdminIdentityOptions.SectionName}:PrivilegedReauthenticationMinutes"] = privilegedReauthenticationMinutes.ToString(),
                    [$"{AdminIdentityOptions.SectionName}:AllowPasswordGrant"] = allowPasswordGrant ? "true" : "false",
                    [$"{AdminIdentityOptions.SectionName}:DisableTransportSecurityRequirement"] = "true",
                    [$"{AdminIdentityOptions.SectionName}:BootstrapUsers:0:UserName"] = "ops-admin",
                    [$"{AdminIdentityOptions.SectionName}:BootstrapUsers:0:Email"] = "ops-admin@example.test",
                    [$"{AdminIdentityOptions.SectionName}:BootstrapUsers:0:Password"] = "Integration123!",
                    [$"{AdminIdentityOptions.SectionName}:BootstrapUsers:0:Role"] = "security-admin",
                    [$"{AdminIdentityOptions.SectionName}:BootstrapUsers:0:Permissions:0"] = AdminPermissionCatalog.DashboardView,
                    [$"{AdminIdentityOptions.SectionName}:BootstrapUsers:0:Permissions:1"] = AdminPermissionCatalog.AuditView,
                    [$"{AdminIdentityOptions.SectionName}:BootstrapUsers:0:Permissions:2"] = AdminPermissionCatalog.TorrentsView,
                    [$"{AdminIdentityOptions.SectionName}:BootstrapUsers:0:Permissions:3"] = AdminPermissionCatalog.TorrentsEdit,
                    [$"{AdminIdentityOptions.SectionName}:BootstrapUsers:0:Permissions:4"] = AdminPermissionCatalog.TrackerPoliciesEdit,
                    [$"{AdminIdentityOptions.SectionName}:BootstrapUsers:0:Permissions:5"] = AdminPermissionCatalog.PasskeysView,
                    [$"{AdminIdentityOptions.SectionName}:BootstrapUsers:0:Permissions:6"] = AdminPermissionCatalog.PasskeysManage,
                    [$"{AdminIdentityOptions.SectionName}:BootstrapUsers:0:Permissions:7"] = AdminPermissionCatalog.TrackerAccessView,
                    [$"{AdminIdentityOptions.SectionName}:BootstrapUsers:0:Permissions:8"] = AdminPermissionCatalog.TrackerAccessManage,
                    [$"{AdminIdentityOptions.SectionName}:BootstrapUsers:0:Permissions:9"] = AdminPermissionCatalog.BansView,
                    [$"{AdminIdentityOptions.SectionName}:BootstrapUsers:0:Permissions:10"] = AdminPermissionCatalog.BansManage,
                    [$"{AdminIdentityOptions.SectionName}:BootstrapUsers:0:Permissions:11"] = AdminPermissionCatalog.NodesView,
                    [$"{AdminIdentityOptions.SectionName}:BootstrapUsers:0:Permissions:12"] = AdminPermissionCatalog.StatsView,
                    [$"{AdminIdentityOptions.SectionName}:BootstrapUsers:0:Permissions:13"] = AdminPermissionCatalog.SystemSettingsView,
                    [$"{AdminIdentityOptions.SectionName}:BootstrapUsers:0:Permissions:14"] = AdminPermissionCatalog.MaintenanceExecute,
                    [$"{AdminIdentityOptions.SectionName}:BootstrapUsers:1:UserName"] = "rbac-root",
                    [$"{AdminIdentityOptions.SectionName}:BootstrapUsers:1:Email"] = "rbac-root@example.test",
                    [$"{AdminIdentityOptions.SectionName}:BootstrapUsers:1:Password"] = "Integration123!",
                    [$"{AdminIdentityOptions.SectionName}:BootstrapUsers:1:Role"] = "SuperAdmin",
                    [$"{AdminIdentityOptions.SectionName}:BootstrapClients:0:ClientId"] = "admin-ui",
                    [$"{AdminIdentityOptions.SectionName}:BootstrapClients:0:DisplayName"] = "Admin UI",
                    [$"{AdminIdentityOptions.SectionName}:BootstrapClients:0:RequirePkce"] = "true",
                    [$"{AdminIdentityOptions.SectionName}:BootstrapClients:0:RedirectUris:0"] = "https://app.example.test/signin-oidc",
                    [$"{AdminIdentityOptions.SectionName}:BootstrapClients:0:PostLogoutRedirectUris:0"] = "https://app.example.test/signout-callback-oidc",
                    [$"{AdminIdentityOptions.SectionName}:BootstrapClients:0:Scopes:0"] = AdminApiScope
                });
            });

            if (timeProvider is not null)
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton(timeProvider);
                });
            }
        }
    }

    private HttpRequestMessage CreateAdminRequest(HttpMethod method, string uri, string correlationId)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
        if (!HttpMethod.Get.Method.Equals(method.Method, StringComparison.OrdinalIgnoreCase) &&
            !HttpMethod.Head.Method.Equals(method.Method, StringComparison.OrdinalIgnoreCase) &&
            !"OPTIONS".Equals(method.Method, StringComparison.OrdinalIgnoreCase) &&
            !"TRACE".Equals(method.Method, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(_csrfToken))
        {
            request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", _csrfToken);
        }

        return request;
    }

}

file sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
}
