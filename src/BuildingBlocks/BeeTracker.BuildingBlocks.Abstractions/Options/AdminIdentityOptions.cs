namespace BeeTracker.BuildingBlocks.Abstractions.Options;

public sealed class AdminIdentityOptions
{
    public const string SectionName = "BeeTracker:AdminIdentity";

    public string AdminApiScope { get; init; } = "admin_api";

    public string SpaClientId { get; init; } = "beetracker-admin-ui";

    public string SpaRedirectPath { get; init; } = "/oidc/callback";

    public string SpaPostLogoutPath { get; init; } = "/";

    public int AccessTokenLifetimeMinutes { get; init; } = 30;

    public int SessionIdleTimeoutMinutes { get; init; } = 60;

    public int PrivilegedReauthenticationMinutes { get; init; } = 15;

    public bool AllowPasswordGrant { get; init; }

    public bool DisableTransportSecurityRequirement { get; init; }

    public IList<AdminBootstrapUserOptions> BootstrapUsers { get; init; } = [];

    public IList<AdminBootstrapClientOptions> BootstrapClients { get; init; } = [];
}

public sealed class AdminBootstrapUserOptions
{
    public string UserName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string Role { get; init; } = "viewer";

    public IList<string> Permissions { get; init; } = [];
}

public sealed class AdminBootstrapClientOptions
{
    public string ClientId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public bool RequirePkce { get; init; } = true;

    public IList<string> RedirectUris { get; init; } = [];

    public IList<string> PostLogoutRedirectUris { get; init; } = [];

    public IList<string> Scopes { get; init; } = [];
}
