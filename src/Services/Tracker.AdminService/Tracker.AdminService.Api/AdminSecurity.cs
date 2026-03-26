using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using BeeTracker.BuildingBlocks.Abstractions.Options;
using BeeTracker.Contracts.Configuration;
using BeeTracker.Contracts.Identity;
using Identity.SelfService.Application;
using Tracker.AdminService.Application;
using Tracker.AdminService.Infrastructure;

using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tracker.AdminService.Api;

internal static class AdminSecurityServiceCollectionExtensions
{
    public static IServiceCollection AddAdminApiAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AdminIdentityOptions>()
            .Bind(configuration.GetSection(AdminIdentityOptions.SectionName))
            .Validate(static options => options.AccessTokenLifetimeMinutes > 0, "Access token lifetime must be positive.")
            .Validate(static options => options.SessionIdleTimeoutMinutes > 0, "Session idle timeout must be positive.")
            .Validate(static options => options.PrivilegedReauthenticationMinutes > 0, "Privileged reauthentication window must be positive.")
            .Validate(static options => !string.IsNullOrWhiteSpace(options.AdminApiScope), "Admin API scope must be configured.")
            .Validate(static options => !string.IsNullOrWhiteSpace(options.SpaClientId), "Admin SPA client id must be configured.")
            .Validate(static options => !string.IsNullOrWhiteSpace(options.SpaRedirectPath) && options.SpaRedirectPath.StartsWith('/'), "Admin SPA redirect path must be a local absolute path.")
            .Validate(static options => !string.IsNullOrWhiteSpace(options.SpaPostLogoutPath) && options.SpaPostLogoutPath.StartsWith('/'), "Admin SPA post-logout path must be a local absolute path.")
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
            options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
            options.DefaultScheme = IdentityConstants.ApplicationScheme;
        });

        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "beetracker_admin_csrf";
            // HttpOnly = false so the SPA JavaScript can read and submit the CSRF token.
            options.Cookie.HttpOnly = false;
            options.Cookie.SameSite = SameSiteMode.Strict;
            // Always — the admin surface is served over HTTPS (via Nginx).
            // SameAsRequest would mark the cookie as non-Secure when ASP.NET Core
            // receives the request over plain HTTP from the reverse proxy.
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        });

        services.AddAuthorization(options =>
        {
            foreach (var (permissionKey, _, _, _) in AdminPermissionCatalog.All)
            {
                options.AddPolicy(
                    AdminAuthorizationPolicies.ForPermission(permissionKey),
                    BuildPermissionPolicy(
                        permissionKey,
                        requireRecentAuthentication: AdminPermissionCatalog.PrivilegedPermissions.Contains(permissionKey)));
            }
        });
        services.AddSingleton<IAuthorizationHandler, RecentAdminAuthenticationHandler>();
        services.AddScoped<IAuthorizationHandler, CurrentPermissionSnapshotHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, AdminAuthorizationResultHandler>();

        var openIddict = services.AddOpenIddict();
        openIddict.AddCore(options =>
        {
            options.UseEntityFrameworkCore()
                .UseDbContext<AdminIdentityDbContext>();
        });
        openIddict
            .AddServer(options =>
            {
                var adminIdentityOptions = configuration.GetSection(AdminIdentityOptions.SectionName).Get<AdminIdentityOptions>() ?? new AdminIdentityOptions();

                options.SetTokenEndpointUris("/connect/token");
                options.SetAuthorizationEndpointUris("/connect/authorize");
                if (adminIdentityOptions.AllowPasswordGrant)
                {
                    options.AllowPasswordFlow();
                }

                options.AllowAuthorizationCodeFlow();
                options.AcceptAnonymousClients();
                options.RegisterScopes(Scopes.OpenId, Scopes.Profile, Scopes.Roles, adminIdentityOptions.AdminApiScope);
                options.UseReferenceAccessTokens();
                options.AddEphemeralEncryptionKey();
                options.AddEphemeralSigningKey();
                options.SetAccessTokenLifetime(TimeSpan.FromMinutes(adminIdentityOptions.AccessTokenLifetimeMinutes));
                var aspNetCore = options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough();

                if (adminIdentityOptions.DisableTransportSecurityRequirement)
                {
                    aspNetCore.DisableTransportSecurityRequirement();
                }
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
            });

        return services;
    }

    private static AuthorizationPolicy BuildPermissionPolicy(string permission, bool requireRecentAuthentication = false)
    {
        var builder = new AuthorizationPolicyBuilder(
                IdentityConstants.ApplicationScheme,
                OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireClaim(AdminClaimTypes.Permission, permission)
            .AddRequirements(new CurrentPermissionSnapshotRequirement());

        if (requireRecentAuthentication)
        {
            builder.AddRequirements(new RecentAdminAuthenticationRequirement());
        }

        return builder.Build();
    }
}

internal sealed class RecentAdminAuthenticationRequirement : IAuthorizationRequirement;
internal sealed class CurrentPermissionSnapshotRequirement : IAuthorizationRequirement;

internal sealed class RecentAdminAuthenticationHandler(
    IOptions<AdminIdentityOptions> options,
    TimeProvider timeProvider) : AuthorizationHandler<RecentAdminAuthenticationRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, RecentAdminAuthenticationRequirement requirement)
    {
        var principal = context.User;
        if (principal.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        var authenticatedAt = AdminSessionState.TryGetAuthenticatedAtUtc(principal);
        if (authenticatedAt is null)
        {
            context.Fail(new AuthorizationFailureReason(this, AdminAuthorizationFailureReasons.ReauthenticationRequired));
            return Task.CompletedTask;
        }

        var now = timeProvider.GetUtcNow();
        var maxAge = TimeSpan.FromMinutes(options.Value.PrivilegedReauthenticationMinutes);
        if (now - authenticatedAt.Value > maxAge)
        {
            context.Fail(new AuthorizationFailureReason(this, AdminAuthorizationFailureReasons.ReauthenticationRequired));
            return Task.CompletedTask;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

internal sealed class CurrentPermissionSnapshotHandler(
    IRbacService rbacService) : AuthorizationHandler<CurrentPermissionSnapshotRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, CurrentPermissionSnapshotRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var rawVersion = context.User.FindFirstValue(AdminClaimTypes.PermissionSnapshotVersion);
        if (!long.TryParse(rawVersion, out var principalVersion))
        {
            context.Fail(new AuthorizationFailureReason(this, AdminAuthorizationFailureReasons.PermissionSnapshotExpired));
            return;
        }

        var currentVersion = await rbacService.GetPermissionSnapshotVersionAsync(CancellationToken.None);
        if (principalVersion != currentVersion)
        {
            context.Fail(new AuthorizationFailureReason(this, AdminAuthorizationFailureReasons.PermissionSnapshotExpired));
            return;
        }

        context.Succeed(requirement);
    }
}

internal static class AdminAuthorization
{
    public static AdminMutationContext CreateMutationContext(HttpContext httpContext)
    {
        var correlationId = httpContext.Request.Headers["X-Correlation-Id"].ToString();

        return new AdminMutationContext(
            httpContext.User.FindFirstValue(Claims.Name)
                ?? httpContext.User.FindFirstValue(ClaimTypes.Name)
                ?? httpContext.User.FindFirstValue(Claims.Subject)
                ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? "unknown",
            httpContext.User.FindFirstValue(Claims.Role)
                ?? httpContext.User.FindFirstValue(ClaimTypes.Role)
                ?? "viewer",
            string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId,
            httpContext.TraceIdentifier,
            httpContext.Connection.RemoteIpAddress?.ToString(),
            httpContext.Request.Headers.UserAgent.ToString());
    }
}

internal static class AdminTokenEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        UserManager<IdentityUser> userManager,
        IRbacService rbacService,
        IAdminAccountRepository adminAccountRepository,
        IOptions<AdminIdentityOptions> adminIdentityOptionsAccessor,
        TimeProvider timeProvider)
    {
        var request = Microsoft.AspNetCore.OpenIddictServerAspNetCoreHelpers.GetOpenIddictServerRequest(httpContext);
        if (request is null)
        {
            return Results.BadRequest(new
            {
                error = Errors.InvalidRequest,
                error_description = "The OpenIddict request could not be resolved."
            });
        }

        var adminIdentityOptions = adminIdentityOptionsAccessor.Value;

        if (request.IsAuthorizationCodeGrantType())
        {
            var result = await httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
            if (!result.Succeeded || result.Principal is null)
            {
                return Results.Forbid(
                    new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The authorization code is no longer associated with an authenticated user session."
                    }),
                    [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
            }

            var authenticatedUser = await userManager.GetUserAsync(result.Principal);
            if (authenticatedUser is null)
            {
                return Results.Forbid(
                    new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The authenticated admin user could not be resolved."
                    }),
                    [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
            }

            if (!await IsEligibleForAdminSignInAsync(adminAccountRepository, authenticatedUser.Id, httpContext.RequestAborted))
            {
                return Results.Forbid(
                    new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The admin account is not in an active state."
                    }),
                    [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
            }

            var authenticatedAtUtc = AdminSessionState.TryGetAuthenticatedAtUtc(result.Principal) ?? timeProvider.GetUtcNow();
            var authorizationCodePrincipal = await CreatePrincipalAsync(userManager, rbacService, authenticatedUser, authenticatedAtUtc, httpContext.RequestAborted);
            authorizationCodePrincipal.SetScopes(GetRequestedScopes(request, adminIdentityOptions));

            return Results.SignIn(authorizationCodePrincipal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (!request.IsPasswordGrantType())
        {
            return Results.BadRequest(new
            {
                error = Errors.UnsupportedGrantType,
                error_description = "Only the authorization_code and optional password grants are supported by this admin plane."
            });
        }

        if (!adminIdentityOptions.AllowPasswordGrant)
        {
            return Results.BadRequest(new
            {
                error = Errors.UnsupportedGrantType,
                error_description = "The password grant is disabled for this admin plane."
            });
        }

        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.BadRequest(new
            {
                error = Errors.InvalidRequest,
                error_description = "The username and password must be provided."
            });
        }

        var user = await userManager.FindByNameAsync(request.Username);
        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
        {
            return Results.Forbid(
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The username/password couple is invalid."
                }),
                [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        if (!await IsEligibleForAdminSignInAsync(adminAccountRepository, user.Id, httpContext.RequestAborted))
        {
            return Results.Forbid(
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The admin account is not in an active state."
                }),
                [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        var principal = await CreatePrincipalAsync(userManager, rbacService, user, timeProvider.GetUtcNow(), httpContext.RequestAborted);
        principal.SetScopes([Scopes.OpenId, Scopes.Profile, Scopes.Roles, adminIdentityOptions.AdminApiScope]);

        return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    public static async Task<IResult> HandleAuthorizationAsync(
        HttpContext httpContext,
        UserManager<IdentityUser> userManager,
        IRbacService rbacService,
        IAdminAccountRepository adminAccountRepository,
        IOptions<AdminIdentityOptions> adminIdentityOptionsAccessor,
        TimeProvider timeProvider)
    {
        var request = Microsoft.AspNetCore.OpenIddictServerAspNetCoreHelpers.GetOpenIddictServerRequest(httpContext);
        if (request is null)
        {
            return Results.BadRequest(new
            {
                error = Errors.InvalidRequest,
                error_description = "The OpenIddict request could not be resolved."
            });
        }

        var authenticateResult = await httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!authenticateResult.Succeeded || authenticateResult.Principal is null)
        {
            var returnUrl = httpContext.Request.PathBase + httpContext.Request.Path + httpContext.Request.QueryString;
            return Results.Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl
                },
                [IdentityConstants.ApplicationScheme]);
        }

        var user = await userManager.GetUserAsync(authenticateResult.Principal);
        if (user is null)
        {
            await httpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return Results.Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = "/account/login"
                },
                [IdentityConstants.ApplicationScheme]);
        }

        if (!await IsEligibleForAdminSignInAsync(adminAccountRepository, user.Id, httpContext.RequestAborted))
        {
            await httpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return Results.Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = "/account/login"
                },
                [IdentityConstants.ApplicationScheme]);
        }

        var authenticatedAtUtc = AdminSessionState.TryGetAuthenticatedAtUtc(authenticateResult.Principal) ?? timeProvider.GetUtcNow();
        var principal = await CreatePrincipalAsync(userManager, rbacService, user, authenticatedAtUtc, httpContext.RequestAborted);
        principal.SetScopes(GetRequestedScopes(request, adminIdentityOptionsAccessor.Value));

        return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    public static IResult RenderLoginPage(HttpContext httpContext, string? returnUrl, bool? reauth)
    {
        var requiresReauthentication = reauth == true;
        if (httpContext.User.Identity?.IsAuthenticated == true && !requiresReauthentication && AdminReauthentication.IsLocalReturnUrl(returnUrl))
        {
            return Results.Redirect(returnUrl!);
        }

        var locale = ResolveLoginLocale(httpContext);
        var copy = GetLoginPageCopy(locale, requiresReauthentication);
        var safeReturnUrl = AdminReauthentication.IsLocalReturnUrl(returnUrl) ? returnUrl! : "/";
        var encodedReturnUrl = HtmlEncoder.Default.Encode(safeReturnUrl);
        var encodedReauth = requiresReauthentication ? "true" : "false";
        var encodedTitle = HtmlEncoder.Default.Encode(copy.Title);
        var encodedSubtitle = HtmlEncoder.Default.Encode(copy.Subtitle);
        var encodedUsername = HtmlEncoder.Default.Encode(copy.UsernameLabel);
        var encodedPassword = HtmlEncoder.Default.Encode(copy.PasswordLabel);
        var encodedButton = HtmlEncoder.Default.Encode(copy.SignInButton);
        var encodedReauthBanner = requiresReauthentication
            ? $"<p class=\"notice\">{HtmlEncoder.Default.Encode(copy.ReauthenticationMessage)}</p>"
            : string.Empty;

        var html =
            $$"""
            <!doctype html>
            <html lang="{{locale}}">
            <head>
              <meta charset="utf-8" />
              <title>{{encodedTitle}}</title>
              <meta name="viewport" content="width=device-width, initial-scale=1" />
              <style>
                :root {
                  color: #0f172a;
                  background: #f8fafc;
                }
                * { box-sizing: border-box; }
                body {
                  margin: 0;
                  min-height: 100vh;
                  font-family: "IBM Plex Sans", "Segoe UI", sans-serif;
                  color: #0f172a;
                  background: radial-gradient(circle at top, rgba(59, 130, 246, 0.10), transparent 28%), #f8fafc;
                }
                .page {
                  min-height: 100vh;
                  display: flex;
                  align-items: center;
                  justify-content: center;
                  padding: 24px;
                }
                .card {
                  width: 100%;
                  max-width: 460px;
                  border: 1px solid rgba(15, 23, 42, 0.08);
                  border-radius: 28px;
                  background: #ffffff;
                  box-shadow: 0 20px 48px rgba(15, 23, 42, 0.08);
                  overflow: hidden;
                }
                .card-head {
                  padding: 28px 28px 20px;
                  border-bottom: 1px solid rgba(148, 163, 184, 0.18);
                }
                .card-kicker {
                  margin: 0;
                  font-size: 11px;
                  font-weight: 700;
                  letter-spacing: 0.22em;
                  text-transform: uppercase;
                  color: #64748b;
                }
                .title {
                  margin: 12px 0 0;
                  font-size: 34px;
                  line-height: 1.12;
                  font-weight: 700;
                  color: #0f172a;
                }
                .auth-body {
                  padding: 24px 28px 28px;
                }
                h1, p { margin-top: 0; }
                .subtitle {
                  margin: 14px 0 0;
                  color: #475569;
                  font-size: 14px;
                  line-height: 1.75;
                }
                label {
                  display: block;
                  margin-top: 18px;
                  font-size: 13px;
                  font-weight: 700;
                  color: #334155;
                }
                input {
                  width: 100%;
                  padding: 14px 16px;
                  margin-top: 8px;
                  border: 1px solid #cbd5e1;
                  border-radius: 18px;
                  font-size: 14px;
                  color: #0f172a;
                  background: #ffffff;
                  outline: none;
                  transition: border-color .15s ease, box-shadow .15s ease;
                }
                input:focus {
                  border-color: #1d4ed8;
                  box-shadow: 0 0 0 4px rgba(29, 78, 216, 0.10);
                }
                .button {
                  margin-top: 24px;
                  width: 100%;
                  padding: 15px 18px;
                  border: 0;
                  border-radius: 18px;
                  background: #0f172a;
                  color: white;
                  font-size: 13px;
                  font-weight: 700;
                  letter-spacing: 0.18em;
                  text-transform: uppercase;
                  cursor: pointer;
                  transition: background .15s ease;
                }
                .button:hover { background: #020617; }
                .notice {
                  margin: 0 0 18px;
                  padding: 13px 14px;
                  border: 1px solid rgba(180, 83, 9, 0.16);
                  border-radius: 16px;
                  background: #fff7ed;
                  color: #b91c1c;
                  font-size: 13px;
                  line-height: 1.6;
                }
              </style>
            </head>
            <body>
              <main class="page">
                <section class="card">
                  <div class="card-head">
                    <p class="card-kicker">BeeTracker</p>
                    <h1 class="title">{{encodedTitle}}</h1>
                    <p class="subtitle">{{encodedSubtitle}}</p>
                  </div>
                  <div class="auth-body">
                    {{encodedReauthBanner}}
                    <form method="post" action="/account/login">
                      <input type="hidden" name="returnUrl" value="{{encodedReturnUrl}}" />
                      <input type="hidden" name="reauth" value="{{encodedReauth}}" />
                      <label for="username">{{encodedUsername}}</label>
                      <input id="username" name="username" autocomplete="username" required />
                      <label for="password">{{encodedPassword}}</label>
                      <input id="password" name="password" type="password" autocomplete="current-password" required />
                      <button class="button" type="submit">{{encodedButton}}</button>
                    </form>
                  </div>
                </section>
              </main>
            </body>
            </html>
            """;

        return Results.Content(html, "text/html; charset=utf-8");
    }

    private static string ResolveLoginLocale(HttpContext httpContext)
    {
        var header = httpContext.Request.Headers.AcceptLanguage.ToString();
        if (string.IsNullOrWhiteSpace(header))
        {
            return "en";
        }

        var languages = header.Split(',', ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var language = languages.Length > 0 ? languages[0] : null;

        if (string.IsNullOrWhiteSpace(language))
        {
            return "en";
        }

        var normalized = language.Length >= 2
            ? language[..2].ToLowerInvariant()
            : language.ToLowerInvariant();

        return normalized is "cs" or "de" or "es" or "fr"
            ? normalized
            : "en";
    }

    private static LoginPageCopy GetLoginPageCopy(string locale, bool requiresReauthentication) => locale switch
    {
        "cs" => new(
            "Přihlášení do BeeTracker",
            "Přihlaste se",
            "Uživatelské jméno",
            "Heslo",
            "Pokračovat",
            "Pro tuto akci je potřeba nové ověření. Přihlaste se znovu a pokračujte."),
        "de" => new(
            "BeeTracker-Anmeldung",
            "Melden Sie sich an",
            "Benutzername",
            "Passwort",
            "Weiter",
            "Für diese Aktion ist eine erneute Anmeldung erforderlich. Melden Sie sich erneut an und fahren Sie fort."),
        "es" => new(
            "Acceso a BeeTracker",
            "Inicia sesión",
            "Usuario",
            "Contraseña",
            "Continuar",
            "Esta acción requiere una autenticación reciente. Vuelve a iniciar sesión para continuar."),
        "fr" => new(
            "Connexion à BeeTracker",
            "Connectez-vous",
            "Nom d’utilisateur",
            "Mot de passe",
            "Continuer",
            "Cette action nécessite une authentification récente. Reconnectez-vous pour continuer."),
        _ => new(
            "Sign in to BeeTracker",
            "Sign in",
            "Username",
            "Password",
            "Continue",
            "Recent authentication is required for this action. Sign in again to continue.")
    };

    private sealed record LoginPageCopy(
        string Title,
        string Subtitle,
        string UsernameLabel,
        string PasswordLabel,
        string SignInButton,
        string ReauthenticationMessage);

    public static async Task<IResult> HandleLoginPostAsync(
        HttpContext httpContext,
        SignInManager<IdentityUser> signInManager,
        UserManager<IdentityUser> userManager,
        IAdminAccountRepository adminAccountRepository,
        IRbacService rbacService,
        TimeProvider timeProvider)
    {
        var form = await httpContext.Request.ReadFormAsync(httpContext.RequestAborted);
        var username = form["username"].ToString();
        var password = form["password"].ToString();
        var returnUrl = form["returnUrl"].ToString();
        var reauth = string.Equals(form["reauth"].ToString(), "true", StringComparison.OrdinalIgnoreCase);
        var safeReturnUrl = AdminReauthentication.IsLocalReturnUrl(returnUrl) ? returnUrl! : "/";
        var user = await userManager.FindByNameAsync(username);
        if (user is null)
        {
            return Results.BadRequest(new
            {
                code = "invalid_credentials",
                message = "The supplied credentials are invalid."
            });
        }

        var signInResult = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (!signInResult.Succeeded)
        {
            return Results.BadRequest(new
            {
                code = "invalid_credentials",
                message = "The supplied credentials are invalid."
            });
        }

        if (!await IsEligibleForAdminSignInAsync(adminAccountRepository, user.Id, httpContext.RequestAborted))
        {
            return Results.BadRequest(new
            {
                code = "inactive_account",
                message = "The admin account is not in an active state."
            });
        }

        if (reauth)
        {
            await httpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }

        var principal = await CreatePrincipalAsync(
            userManager,
            rbacService,
            user,
            timeProvider.GetUtcNow(),
            httpContext.RequestAborted);
        await httpContext.SignInAsync(
            IdentityConstants.ApplicationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = false
            });

        return Results.Redirect(safeReturnUrl);
    }

    public static async Task<IResult> HandleLogoutPostAsync(HttpContext httpContext)
    {
        string? returnUrl = null;
        if (httpContext.Request.HasFormContentType)
        {
            var form = await httpContext.Request.ReadFormAsync(httpContext.RequestAborted);
            returnUrl = form["returnUrl"].ToString();
        }

        await httpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        return AdminReauthentication.IsLocalReturnUrl(returnUrl)
            ? Results.Redirect(returnUrl!)
            : Results.NoContent();
    }

    private static async Task<ClaimsPrincipal> CreatePrincipalAsync(
        UserManager<IdentityUser> userManager,
        IRbacService rbacService,
        IdentityUser user,
        DateTimeOffset authenticatedAtUtc,
        CancellationToken cancellationToken)
    {
        var identity = new ClaimsIdentity(
            authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.AddClaim(CreateAccessTokenClaim(Claims.Subject, user.Id));
        identity.AddClaim(CreateAccessTokenClaim(ClaimTypes.NameIdentifier, user.Id));
        identity.AddClaim(CreateAccessTokenClaim(Claims.Name, user.UserName ?? user.Id));
        identity.AddClaim(CreateAccessTokenClaim(ClaimTypes.Name, user.UserName ?? user.Id));
        identity.AddClaim(CreateAccessTokenClaim(
            AdminClaimTypes.AuthenticatedAt,
            authenticatedAtUtc.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)));

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            identity.AddClaim(CreateAccessTokenClaim(Claims.Email, user.Email));
            identity.AddClaim(CreateAccessTokenClaim(ClaimTypes.Email, user.Email));
        }

        foreach (var role in await userManager.GetRolesAsync(user))
        {
            identity.AddClaim(CreateAccessTokenClaim(Claims.Role, role));
            identity.AddClaim(CreateAccessTokenClaim(ClaimTypes.Role, role));
        }

        var directPermissionClaims = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var claim in await userManager.GetClaimsAsync(user))
        {
            identity.AddClaim(CreateAccessTokenClaim(claim.Type, claim.Value));
            if (string.Equals(claim.Type, AdminClaimTypes.Permission, StringComparison.Ordinal))
            {
                directPermissionClaims.Add(claim.Value);
            }
        }

        var effectivePermissions = await rbacService.GetEffectivePermissionsAsync(user.Id, cancellationToken);
        var grantedPermissions = effectivePermissions
            .Union(directPermissionClaims, StringComparer.OrdinalIgnoreCase);

        foreach (var permission in grantedPermissions.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))
        {
            identity.AddClaim(CreateAccessTokenClaim(AdminClaimTypes.Permission, permission));
        }

        identity.AddClaim(CreateAccessTokenClaim(
            AdminClaimTypes.PermissionSnapshotVersion,
            (await rbacService.GetPermissionSnapshotVersionAsync(cancellationToken)).ToString(System.Globalization.CultureInfo.InvariantCulture)));

        return new ClaimsPrincipal(identity);
    }

    private static async Task<bool> IsEligibleForAdminSignInAsync(IAdminAccountRepository adminAccountRepository, string userId, CancellationToken cancellationToken)
    {
        var accountState = await adminAccountRepository.GetAccountStateAsync(userId, cancellationToken);
        return accountState is null or Identity.SelfService.Domain.AdminAccountState.Active;
    }

    private static Claim CreateAccessTokenClaim(string type, string value)
    {
        var claim = new Claim(type, value);
        claim.SetDestinations(Destinations.AccessToken, Destinations.IdentityToken);
        return claim;
    }

    private static IEnumerable<string> GetRequestedScopes(OpenIddictRequest request, AdminIdentityOptions options)
    {
        var scopes = request.GetScopes().Where(static scope => !string.IsNullOrWhiteSpace(scope)).ToHashSet(StringComparer.Ordinal);
        scopes.Add(Scopes.OpenId);
        scopes.Add(Scopes.Profile);
        scopes.Add(Scopes.Roles);
        scopes.Add(options.AdminApiScope);

        return scopes;
    }

}

internal sealed class AdminAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged)
        {
            if (context.Request.Path.StartsWithSegments("/api/admin", StringComparison.Ordinal))
            {
                var returnUrl = AdminReauthentication.ResolveReturnUrl(context);
                var reauthenticationUrl = AdminReauthentication.BuildLoginPath(returnUrl);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "admin_unauthorized",
                    message = "An authenticated admin session is required.",
                    reauthenticationUrl,
                    reauthenticationContext = AdminReauthentication.BuildContext(
                        reason: AdminReauthenticationReasons.SessionMissing,
                        action: $"{context.Request.Method} {context.Request.Path}",
                        returnUrl,
                        reauthenticationUrl,
                        severity: AdminReauthentication.ResolveSeverity($"{context.Request.Method} {context.Request.Path}", returnUrl))
                });
                return;
            }
        }

        if (authorizeResult.Forbidden)
        {
            if (authorizeResult.AuthorizationFailure?.FailureReasons.Any(static reason => string.Equals(reason.Message, AdminAuthorizationFailureReasons.ReauthenticationRequired, StringComparison.Ordinal)) == true)
            {
                var returnUrl = AdminReauthentication.ResolveReturnUrl(context);
                var reauthenticationUrl = AdminReauthentication.BuildLoginPath(returnUrl);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "admin_reauthentication_required",
                    message = "Recent authentication is required for this privileged admin operation.",
                    reauthenticationUrl,
                    reauthenticationContext = AdminReauthentication.BuildContext(
                        reason: AdminReauthenticationReasons.SessionStale,
                        action: $"{context.Request.Method} {context.Request.Path}",
                        returnUrl,
                        reauthenticationUrl,
                        severity: AdminReauthentication.ResolveSeverity($"{context.Request.Method} {context.Request.Path}", returnUrl))
                });
                return;
            }

            if (authorizeResult.AuthorizationFailure?.FailureReasons.Any(static reason => string.Equals(reason.Message, AdminAuthorizationFailureReasons.PermissionSnapshotExpired, StringComparison.Ordinal)) == true)
            {
                var returnUrl = AdminReauthentication.ResolveReturnUrl(context);
                var reauthenticationUrl = AdminReauthentication.BuildLoginPath(returnUrl);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "admin_permission_snapshot_expired",
                    message = "The current admin session is stale because permissions changed. Sign in again to refresh authorization.",
                    reauthenticationUrl,
                    reauthenticationContext = AdminReauthentication.BuildContext(
                        reason: AdminReauthenticationReasons.SessionStale,
                        action: $"{context.Request.Method} {context.Request.Path}",
                        returnUrl,
                        reauthenticationUrl,
                        severity: AdminReauthentication.ResolveSeverity($"{context.Request.Method} {context.Request.Path}", returnUrl))
                });
                return;
            }

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "admin_forbidden",
                message = "The current admin principal does not satisfy the required permission policy.",
                actorId = context.User.FindFirstValue(Claims.Subject) ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown",
                role = context.User.FindFirstValue(Claims.Role) ?? "unknown"
            });
            return;
        }

        await _defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }
}

internal static class AdminAuthorizationFailureReasons
{
    public const string ReauthenticationRequired = "recent_authentication_required";
    public const string PermissionSnapshotExpired = "permission_snapshot_expired";
}

internal static class AdminReauthenticationReasons
{
    public const string PrivilegedAction = "privileged_action";
    public const string SessionMissing = "session_missing";
    public const string SessionStale = "session_stale";
}

internal static class AdminReauthenticationSeverities
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
}

internal static class AdminSessionState
{
    public static DateTimeOffset? TryGetAuthenticatedAtUtc(ClaimsPrincipal principal)
    {
        var rawValue = principal.FindFirstValue(AdminClaimTypes.AuthenticatedAt);
        if (!long.TryParse(rawValue, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var epochSeconds))
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(epochSeconds);
    }

    public static long? TryGetPermissionSnapshotVersion(ClaimsPrincipal principal)
    {
        var rawValue = principal.FindFirstValue(AdminClaimTypes.PermissionSnapshotVersion);
        return long.TryParse(rawValue, out var version) ? version : null;
    }

    public static DateTimeOffset? GetPrivilegedSessionFreshUntilUtc(ClaimsPrincipal principal, AdminIdentityOptions options)
    {
        var authenticatedAt = TryGetAuthenticatedAtUtc(principal);
        if (authenticatedAt is null)
        {
            return null;
        }

        return authenticatedAt.Value.AddMinutes(options.PrivilegedReauthenticationMinutes);
    }
}

internal static class AdminReauthentication
{
    public static AdminReauthenticationContext BuildContext(string reason, string action, string returnUrl, string reauthenticationUrl, string severity)
        => new(
            reason,
            action,
            IsLocalReturnUrl(returnUrl) ? returnUrl : "/",
            reauthenticationUrl,
            severity);

    public static string ResolveSeverity(string action, string returnUrl)
    {
        var candidate = $"{action} {returnUrl}";
        if (candidate.Contains("/passkeys", StringComparison.OrdinalIgnoreCase) ||
            candidate.Contains("/permissions", StringComparison.OrdinalIgnoreCase) ||
            candidate.Contains("/bans", StringComparison.OrdinalIgnoreCase) ||
            candidate.Contains("/maintenance", StringComparison.OrdinalIgnoreCase))
        {
            return AdminReauthenticationSeverities.High;
        }

        if (candidate.Contains("/torrents", StringComparison.OrdinalIgnoreCase))
        {
            return AdminReauthenticationSeverities.Medium;
        }

        return AdminReauthenticationSeverities.Low;
    }

    public static string ResolveReturnUrl(HttpContext httpContext)
    {
        var requestedReturnUrl = httpContext.Request.Query["returnUrl"].ToString();
        if (IsLocalReturnUrl(requestedReturnUrl))
        {
            return requestedReturnUrl;
        }

        requestedReturnUrl = httpContext.Request.Headers["X-Admin-Return-Url"].ToString();
        if (IsLocalReturnUrl(requestedReturnUrl))
        {
            return requestedReturnUrl;
        }

        var referer = httpContext.Request.Headers.Referer.ToString();
        if (Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
        {
            var candidate = refererUri.PathAndQuery;
            if (IsLocalReturnUrl(candidate))
            {
                return candidate;
            }
        }

        return "/";
    }

    public static string BuildLoginPath(string returnUrl)
        => $"/account/login?returnUrl={Uri.EscapeDataString(IsLocalReturnUrl(returnUrl) ? returnUrl : "/")}&reauth=true";

    public static bool IsLocalReturnUrl(string? returnUrl)
        => !string.IsNullOrWhiteSpace(returnUrl) && Uri.TryCreate(returnUrl, UriKind.Relative, out var relativeUri) && relativeUri is not null && returnUrl.StartsWith('/');
}

internal sealed record AdminCapabilityDescriptor(
    string Action,
    string Permission,
    string HttpMethod,
    bool SupportsBulk,
    string? BulkRoutePattern,
    string? DryRunRoutePattern,
    string SelectionMode,
    string IdempotencyHint,
    bool ConfirmationRequired,
    string ConfirmationSeverity,
    bool DryRunSupported,
    bool RequiresPrivilegedReauthentication,
    string Severity,
    string Category,
    string ResourceKind,
    string RoutePattern,
    string DisplayName,
    string ReauthenticationPrompt,
    string PayloadKind,
    int? MaxItems,
    IReadOnlyList<string> SupportedFields,
    IReadOnlyList<string> RequiredFields,
    IReadOnlyDictionary<string, string> FieldTypes,
    string ResponseKind,
    string? ResultItemKind,
    string? ResultCollectionProperty);

internal static class AdminCapabilities
{
    private static readonly AdminCapabilityDescriptor[] Descriptors =
    [
        CreateReadCapability("admin.read.cluster_overview", AdminPermissions.DashboardView, "cluster", "/api/admin/cluster-overview", "View cluster overview", "monitoring"),
        CreateReadCapability("admin.read.audit", AdminPermissions.AuditView, "audit-record", "/api/admin/audit", "View audit history", "audit"),
        CreateReadCapability("admin.read.maintenance", AdminPermissions.SystemSettingsView, "maintenance-run", "/api/admin/maintenance", "View maintenance history", "maintenance"),
        CreateReadCapability("admin.read.torrents", AdminPermissions.TorrentsView, "torrent", "/api/admin/torrents", "View torrents", "configuration"),
        CreateReadCapability("admin.read.passkeys", AdminPermissions.PasskeysView, "passkey", "/api/admin/passkeys", "View passkeys", "access"),
        CreateReadCapability("admin.read.permissions", AdminPermissions.TrackerAccessView, "tracker-access", "/api/admin/tracker-access", "View tracker access rights", "access"),
        CreateReadCapability("admin.read.bans", AdminPermissions.BansView, "ban", "/api/admin/bans", "View bans", "access"),
        CreateWriteCapability("admin.write.torrent_policy", AdminPermissions.TrackerPoliciesEdit, "PUT", false, null, null, "single", "idempotent", true, AdminReauthenticationSeverities.Medium, false, false, AdminReauthenticationSeverities.Medium, "configuration", "torrent-policy", "/api/admin/torrents/{infoHash}/policy", "Edit torrent policy", "This change affects tracker configuration.", "single_payload", null, ["isPrivate", "isEnabled", "announceIntervalSeconds", "minAnnounceIntervalSeconds", "defaultNumWant", "maxNumWant", "allowScrape", "expectedVersion"], ["isPrivate", "isEnabled", "announceIntervalSeconds", "minAnnounceIntervalSeconds", "defaultNumWant", "maxNumWant", "allowScrape"], new Dictionary<string, string>(StringComparer.Ordinal) { ["isPrivate"] = "boolean", ["isEnabled"] = "boolean", ["announceIntervalSeconds"] = "int32", ["minAnnounceIntervalSeconds"] = "int32", ["defaultNumWant"] = "int32", ["maxNumWant"] = "int32", ["allowScrape"] = "boolean", ["expectedVersion"] = "int64?" }, "single_snapshot", "torrent", null),
        CreateWriteCapability("admin.bulk_upsert.torrent_policy", AdminPermissions.TrackerPoliciesEdit, "PUT", true, "/api/admin/torrents/bulk/policy", "/api/admin/torrents/bulk/policy/dry-run", "multi_select", "idempotent", true, AdminReauthenticationSeverities.High, true, false, AdminReauthenticationSeverities.High, "configuration", "torrent-policy", "/api/admin/torrents/{infoHash}/policy", "Bulk edit torrent policies", "This change applies tracker policy updates to multiple torrents.", "bulk_collection", 50, ["infoHash", "isPrivate", "isEnabled", "announceIntervalSeconds", "minAnnounceIntervalSeconds", "defaultNumWant", "maxNumWant", "allowScrape", "expectedVersion"], ["infoHash", "isPrivate", "isEnabled", "announceIntervalSeconds", "minAnnounceIntervalSeconds", "defaultNumWant", "maxNumWant", "allowScrape"], new Dictionary<string, string>(StringComparer.Ordinal) { ["infoHash"] = "string", ["isPrivate"] = "boolean", ["isEnabled"] = "boolean", ["announceIntervalSeconds"] = "int32", ["minAnnounceIntervalSeconds"] = "int32", ["defaultNumWant"] = "int32", ["maxNumWant"] = "int32", ["allowScrape"] = "boolean", ["expectedVersion"] = "int64?" }, "bulk_operation_result", "torrent", "torrentItems"),
        CreateWriteCapability("admin.write.passkey", AdminPermissions.PasskeysManage, "PUT", false, null, null, "single", "idempotent", true, AdminReauthenticationSeverities.High, false, true, AdminReauthenticationSeverities.High, "access", "passkey", "/api/admin/passkeys/{passkey}", "Manage passkeys", "Sign in again to confirm this sensitive passkey change.", "single_payload", null, ["userId", "isRevoked", "expiresAtUtc", "expectedVersion"], ["userId", "isRevoked"], new Dictionary<string, string>(StringComparer.Ordinal) { ["userId"] = "guid", ["isRevoked"] = "boolean", ["expiresAtUtc"] = "datetimeoffset?", ["expectedVersion"] = "int64?" }, "single_snapshot", "passkey", null),
        CreateWriteCapability("admin.revoke.passkey", AdminPermissions.PasskeysManage, "POST", true, "/api/admin/passkeys/bulk/revoke", null, "multi_select", "idempotent", true, AdminReauthenticationSeverities.High, false, true, AdminReauthenticationSeverities.High, "access", "passkey", "/api/admin/passkeys/{passkey}", "Revoke passkeys", "Sign in again to confirm these passkey revocations.", "bulk_collection", 200, ["passkey", "expectedVersion"], ["passkey"], new Dictionary<string, string>(StringComparer.Ordinal) { ["passkey"] = "string", ["expectedVersion"] = "int64?" }, "bulk_operation_result", "passkey", "passkeyItems"),
        CreateWriteCapability("admin.rotate.passkey", AdminPermissions.PasskeysManage, "POST", true, "/api/admin/passkeys/bulk/rotate", null, "multi_select", "non_idempotent", true, AdminReauthenticationSeverities.High, false, true, AdminReauthenticationSeverities.High, "access", "passkey", "/api/admin/passkeys/{passkey}", "Rotate passkeys", "Sign in again to confirm these passkey rotations.", "bulk_collection", 100, ["passkey", "expiresAtUtc", "expectedVersion"], ["passkey"], new Dictionary<string, string>(StringComparer.Ordinal) { ["passkey"] = "string", ["expiresAtUtc"] = "datetimeoffset?", ["expectedVersion"] = "int64?" }, "bulk_operation_result", "passkey", "passkeyItems"),
        CreateWriteCapability("admin.activate.torrent", AdminPermissions.TorrentsEdit, "POST", true, "/api/admin/torrents/bulk/activate", null, "multi_select", "idempotent", true, AdminReauthenticationSeverities.Medium, false, false, AdminReauthenticationSeverities.Medium, "configuration", "torrent", "/api/admin/torrents/{infoHash}", "Activate torrents", "This change enables tracker traffic for the selected torrents.", "bulk_collection", 200, ["infoHash", "expectedVersion"], ["infoHash"], new Dictionary<string, string>(StringComparer.Ordinal) { ["infoHash"] = "string", ["expectedVersion"] = "int64?" }, "bulk_operation_result", "torrent", "torrentItems"),
        CreateWriteCapability("admin.deactivate.torrent", AdminPermissions.TorrentsEdit, "POST", true, "/api/admin/torrents/bulk/deactivate", null, "multi_select", "idempotent", true, AdminReauthenticationSeverities.Medium, false, false, AdminReauthenticationSeverities.Medium, "configuration", "torrent", "/api/admin/torrents/{infoHash}", "Deactivate torrents", "This change disables tracker traffic for the selected torrents.", "bulk_collection", 200, ["infoHash", "expectedVersion"], ["infoHash"], new Dictionary<string, string>(StringComparer.Ordinal) { ["infoHash"] = "string", ["expectedVersion"] = "int64?" }, "bulk_operation_result", "torrent", "torrentItems"),
        CreateWriteCapability("admin.write.permissions", AdminPermissions.TrackerAccessManage, "PUT", true, "/api/admin/users/bulk/tracker-access", null, "multi_select", "idempotent", true, AdminReauthenticationSeverities.High, false, true, AdminReauthenticationSeverities.High, "access", "tracker-access", "/api/admin/users/{userId}/tracker-access", "Manage tracker access rights", "Sign in again to confirm this privileged tracker access change.", "bulk_collection", 200, ["userId", "canLeech", "canSeed", "canScrape", "canUsePrivateTracker", "expectedVersion"], ["userId", "canLeech", "canSeed", "canScrape", "canUsePrivateTracker"], new Dictionary<string, string>(StringComparer.Ordinal) { ["userId"] = "guid", ["canLeech"] = "boolean", ["canSeed"] = "boolean", ["canScrape"] = "boolean", ["canUsePrivateTracker"] = "boolean", ["expectedVersion"] = "int64?" }, "bulk_operation_result", "tracker-access", "trackerAccessItems"),
        CreateWriteCapability("admin.write.ban", AdminPermissions.BansManage, "PUT", true, "/api/admin/bans/bulk", null, "multi_select", "idempotent", true, AdminReauthenticationSeverities.High, false, true, AdminReauthenticationSeverities.High, "access", "ban", "/api/admin/bans/{scope}/{subject}", "Manage bans", "Sign in again to confirm this sensitive ban change.", "bulk_collection", 200, ["scope", "subject", "reason", "expiresAtUtc", "expectedVersion"], ["scope", "subject", "reason"], new Dictionary<string, string>(StringComparer.Ordinal) { ["scope"] = "string", ["subject"] = "string", ["reason"] = "string", ["expiresAtUtc"] = "datetimeoffset?", ["expectedVersion"] = "int64?" }, "bulk_operation_result", "ban", "banItems"),
        CreateWriteCapability("admin.expire.ban", AdminPermissions.BansManage, "POST", true, "/api/admin/bans/bulk/expire", null, "multi_select", "idempotent", true, AdminReauthenticationSeverities.High, false, true, AdminReauthenticationSeverities.High, "access", "ban", "/api/admin/bans/{scope}/{subject}", "Expire bans", "Sign in again to confirm these ban expiry changes.", "bulk_collection", 200, ["scope", "subject", "expiresAtUtc", "expectedVersion"], ["scope", "subject", "expiresAtUtc"], new Dictionary<string, string>(StringComparer.Ordinal) { ["scope"] = "string", ["subject"] = "string", ["expiresAtUtc"] = "datetimeoffset", ["expectedVersion"] = "int64?" }, "bulk_operation_result", "ban", "banItems"),
        CreateWriteCapability("admin.delete.ban", AdminPermissions.BansManage, "POST", true, "/api/admin/bans/bulk/delete", null, "multi_select", "conditional_idempotent", true, AdminReauthenticationSeverities.High, false, true, AdminReauthenticationSeverities.High, "access", "ban", "/api/admin/bans/{scope}/{subject}", "Delete bans", "Sign in again to confirm these ban deletions.", "bulk_collection", 200, ["scope", "subject", "expectedVersion"], ["scope", "subject"], new Dictionary<string, string>(StringComparer.Ordinal) { ["scope"] = "string", ["subject"] = "string", ["expectedVersion"] = "int64?" }, "bulk_operation_result", "ban", "banItems"),
        CreateWriteCapability("admin.execute.maintenance", AdminPermissions.MaintenanceExecute, "POST", false, null, null, "none", "non_idempotent", true, AdminReauthenticationSeverities.High, false, true, AdminReauthenticationSeverities.High, "maintenance", "maintenance-run", "/api/admin/maintenance/cache-refresh", "Run maintenance", "Sign in again to execute a privileged maintenance operation.", "empty", null, [], [], new Dictionary<string, string>(StringComparer.Ordinal), "accepted", null, null)
    ];

    public static IReadOnlyList<AdminCapabilityDescriptor> All => Descriptors;

    private static AdminCapabilityDescriptor CreateReadCapability(string action, string permission, string resourceKind, string routePattern, string displayName, string category)
        => new(action, permission, "GET", false, null, null, "none", "safe", false, "none", false, false, AdminReauthenticationSeverities.Low, category, resourceKind, routePattern, displayName, "No reauthentication required.", "none", null, [], [], new Dictionary<string, string>(StringComparer.Ordinal), "query_result", resourceKind, null);

    private static AdminCapabilityDescriptor CreateWriteCapability(
        string action,
        string permission,
        string httpMethod,
        bool supportsBulk,
        string? bulkRoutePattern,
        string? dryRunRoutePattern,
        string selectionMode,
        string idempotencyHint,
        bool confirmationRequired,
        string confirmationSeverity,
        bool dryRunSupported,
        bool requiresPrivilegedReauthentication,
        string severity,
        string category,
        string resourceKind,
        string routePattern,
        string displayName,
        string reauthenticationPrompt,
        string payloadKind,
        int? maxItems,
        IReadOnlyList<string> supportedFields,
        IReadOnlyList<string> requiredFields,
        IReadOnlyDictionary<string, string> fieldTypes,
        string responseKind,
        string? resultItemKind,
        string? resultCollectionProperty)
        => new(action, permission, httpMethod, supportsBulk, bulkRoutePattern, dryRunRoutePattern, selectionMode, idempotencyHint, confirmationRequired, confirmationSeverity, dryRunSupported, requiresPrivilegedReauthentication, severity, category, resourceKind, routePattern, displayName, reauthenticationPrompt, payloadKind, maxItems, supportedFields, requiredFields, fieldTypes, responseKind, resultItemKind, resultCollectionProperty);
}

internal static class AdminAntiforgery
{
    public static async Task<bool> ValidateUnsafeAdminRequestAsync(HttpContext httpContext)
    {
        if (httpContext.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!httpContext.Request.Path.StartsWithSegments("/api/admin", StringComparison.Ordinal) ||
            HttpMethods.IsGet(httpContext.Request.Method) ||
            HttpMethods.IsHead(httpContext.Request.Method) ||
            HttpMethods.IsOptions(httpContext.Request.Method) ||
            HttpMethods.IsTrace(httpContext.Request.Method))
        {
            return true;
        }

        var antiforgery = httpContext.RequestServices.GetRequiredService<IAntiforgery>();

        try
        {
            await antiforgery.ValidateRequestAsync(httpContext);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsJsonAsync(new
            {
                code = "invalid_csrf_token",
                message = "A valid CSRF token is required for admin mutation requests."
            });
            return false;
        }
    }
}

