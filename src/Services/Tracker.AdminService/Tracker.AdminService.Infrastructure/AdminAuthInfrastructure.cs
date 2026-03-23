using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using Tracker.AdminService.Application;

namespace Tracker.AdminService.Infrastructure;

using static OpenIddict.Abstractions.OpenIddictConstants;

public sealed class AdminIdentityDbContext(DbContextOptions<AdminIdentityDbContext> options)
    : IdentityDbContext<IdentityUser, IdentityRole, string>(options)
{
    public const string SchemaName = "admin_auth";

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(SchemaName);
        base.OnModelCreating(builder);
    }
}

public sealed class AdminIdentitySeedService(
    IServiceScopeFactory serviceScopeFactory,
    IOptions<AdminIdentityOptions> options,
    ILogger<AdminIdentitySeedService> logger)
{
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly IOptions<AdminIdentityOptions> _options = options;
    private readonly ILogger<AdminIdentitySeedService> _logger = logger;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceScopeFactory.CreateScope();

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        foreach (var bootstrapUser in _options.Value.BootstrapUsers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(bootstrapUser.UserName) || string.IsNullOrWhiteSpace(bootstrapUser.Password))
            {
                _logger.LogWarning("Skipping admin bootstrap user with missing username or password.");
                continue;
            }

            var normalizedRole = string.IsNullOrWhiteSpace(bootstrapUser.Role) ? "viewer" : bootstrapUser.Role.Trim();
            if (!await roleManager.RoleExistsAsync(normalizedRole))
            {
                var createRole = await roleManager.CreateAsync(new IdentityRole(normalizedRole));
                if (!createRole.Succeeded)
                {
                    throw new InvalidOperationException($"Failed to create admin role '{normalizedRole}': {string.Join(", ", createRole.Errors.Select(static error => error.Description))}");
                }
            }

            var user = await userManager.FindByNameAsync(bootstrapUser.UserName);
            if (user is null)
            {
                user = new IdentityUser
                {
                    UserName = bootstrapUser.UserName,
                    Email = string.IsNullOrWhiteSpace(bootstrapUser.Email) ? null : bootstrapUser.Email,
                    EmailConfirmed = !string.IsNullOrWhiteSpace(bootstrapUser.Email)
                };

                var createUser = await userManager.CreateAsync(user, bootstrapUser.Password);
                if (!createUser.Succeeded)
                {
                    throw new InvalidOperationException($"Failed to create bootstrap admin user '{bootstrapUser.UserName}': {string.Join(", ", createUser.Errors.Select(static error => error.Description))}");
                }
            }
            else if (!string.Equals(user.Email, bootstrapUser.Email, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(bootstrapUser.Email))
            {
                user.Email = bootstrapUser.Email;
                user.EmailConfirmed = true;
                var updateUser = await userManager.UpdateAsync(user);
                if (!updateUser.Succeeded)
                {
                    throw new InvalidOperationException($"Failed to update bootstrap admin user '{bootstrapUser.UserName}': {string.Join(", ", updateUser.Errors.Select(static error => error.Description))}");
                }
            }

            var currentRoles = await userManager.GetRolesAsync(user);
            foreach (var currentRole in currentRoles.Where(role => !string.Equals(role, normalizedRole, StringComparison.OrdinalIgnoreCase)))
            {
                var removeRole = await userManager.RemoveFromRoleAsync(user, currentRole);
                if (!removeRole.Succeeded)
                {
                    throw new InvalidOperationException($"Failed to remove role '{currentRole}' from bootstrap user '{bootstrapUser.UserName}'.");
                }
            }

            if (!currentRoles.Contains(normalizedRole, StringComparer.OrdinalIgnoreCase))
            {
                var addRole = await userManager.AddToRoleAsync(user, normalizedRole);
                if (!addRole.Succeeded)
                {
                    throw new InvalidOperationException($"Failed to assign role '{normalizedRole}' to bootstrap user '{bootstrapUser.UserName}'.");
                }
            }

            var currentClaims = await userManager.GetClaimsAsync(user);
            foreach (var claim in currentClaims.Where(static claim => string.Equals(claim.Type, AdminClaimTypes.Permission, StringComparison.Ordinal)))
            {
                var removeClaim = await userManager.RemoveClaimAsync(user, claim);
                if (!removeClaim.Succeeded)
                {
                    throw new InvalidOperationException($"Failed to remove permission claim '{claim.Value}' from bootstrap user '{bootstrapUser.UserName}'.");
                }
            }

            foreach (var permission in bootstrapUser.Permissions.Where(static permission => !string.IsNullOrWhiteSpace(permission)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var addClaim = await userManager.AddClaimAsync(user, new System.Security.Claims.Claim(AdminClaimTypes.Permission, permission));
                if (!addClaim.Succeeded)
                {
                    throw new InvalidOperationException($"Failed to assign permission '{permission}' to bootstrap user '{bootstrapUser.UserName}'.");
                }
            }
        }

        foreach (var bootstrapClient in _options.Value.BootstrapClients)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(bootstrapClient.ClientId) || bootstrapClient.RedirectUris.Count == 0)
            {
                _logger.LogWarning("Skipping admin bootstrap client with missing client id or redirect URIs.");
                continue;
            }

            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = bootstrapClient.ClientId,
                DisplayName = string.IsNullOrWhiteSpace(bootstrapClient.DisplayName) ? bootstrapClient.ClientId : bootstrapClient.DisplayName,
                ClientType = ClientTypes.Public,
                ConsentType = ConsentTypes.Implicit
            };

            foreach (var redirectUri in bootstrapClient.RedirectUris.Where(static uri => !string.IsNullOrWhiteSpace(uri)))
            {
                descriptor.RedirectUris.Add(new Uri(redirectUri, UriKind.Absolute));
            }

            foreach (var postLogoutRedirectUri in bootstrapClient.PostLogoutRedirectUris.Where(static uri => !string.IsNullOrWhiteSpace(uri)))
            {
                descriptor.PostLogoutRedirectUris.Add(new Uri(postLogoutRedirectUri, UriKind.Absolute));
            }

            descriptor.Permissions.UnionWith(
            [
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.ResponseTypes.Code
            ]);

            foreach (var requestedScope in bootstrapClient.Scopes.Where(static scope => !string.IsNullOrWhiteSpace(scope)))
            {
                descriptor.Permissions.Add(Permissions.Prefixes.Scope + requestedScope);
            }

            if (bootstrapClient.RequirePkce)
            {
                descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
            }

            var existingApplication = await applicationManager.FindByClientIdAsync(bootstrapClient.ClientId, cancellationToken);
            if (existingApplication is null)
            {
                await applicationManager.CreateAsync(descriptor, cancellationToken);
                continue;
            }

            var existingDescriptor = new OpenIddictApplicationDescriptor();
            await applicationManager.PopulateAsync(existingDescriptor, existingApplication, cancellationToken);

            existingDescriptor.DisplayName = descriptor.DisplayName;
            existingDescriptor.ClientType = descriptor.ClientType;
            existingDescriptor.ConsentType = descriptor.ConsentType;
            existingDescriptor.RedirectUris.Clear();
            existingDescriptor.PostLogoutRedirectUris.Clear();
            existingDescriptor.Permissions.Clear();
            existingDescriptor.Requirements.Clear();

            foreach (var redirectUri in descriptor.RedirectUris)
            {
                existingDescriptor.RedirectUris.Add(redirectUri);
            }

            foreach (var postLogoutRedirectUri in descriptor.PostLogoutRedirectUris)
            {
                existingDescriptor.PostLogoutRedirectUris.Add(postLogoutRedirectUri);
            }

            existingDescriptor.Permissions.UnionWith(descriptor.Permissions);
            existingDescriptor.Requirements.UnionWith(descriptor.Requirements);

            await applicationManager.UpdateAsync(existingApplication, existingDescriptor, cancellationToken);
        }
    }
}

public static class AdminAuthInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddAdminAuthInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AdminIdentityOptions>()
            .Bind(configuration.GetSection(AdminIdentityOptions.SectionName))
            .Validate(static options => options.AccessTokenLifetimeMinutes > 0, "Access token lifetime must be positive.")
            .Validate(static options => options.SessionIdleTimeoutMinutes > 0, "Session idle timeout must be positive.")
            .Validate(static options => options.PrivilegedReauthenticationMinutes > 0, "Privileged reauthentication window must be positive.")
            .ValidateOnStart();

        services.AddDbContext<AdminIdentityDbContext>((serviceProvider, options) =>
        {
            var postgresOptions = serviceProvider.GetRequiredService<IOptions<PostgresOptions>>().Value;
            options.UseNpgsql(
                postgresOptions.ConnectionString,
                npgsqlOptions => npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", AdminIdentityDbContext.SchemaName));
            options.UseOpenIddict();
        });

        services.AddIdentity<IdentityUser, IdentityRole>(options =>
            {
                options.User.RequireUniqueEmail = false;
            })
            .AddEntityFrameworkStores<AdminIdentityDbContext>()
            .AddDefaultTokenProviders();

        var adminIdentityOptions = configuration.GetSection(AdminIdentityOptions.SectionName).Get<AdminIdentityOptions>() ?? new AdminIdentityOptions();

        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/account/login";
            options.Cookie.Name = "swarmcore_admin_auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            // Always — the admin surface is served over HTTPS (via Nginx).
            // SameAsRequest would mark the cookie as non-Secure when ASP.NET Core
            // receives the request over plain HTTP from the reverse proxy.
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(adminIdentityOptions.SessionIdleTimeoutMinutes);
            options.SlidingExpiration = true;
        });

        services.AddScoped<AdminIdentitySeedService>();

        return services;
    }
}
