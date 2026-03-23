using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using Swarmcore.BuildingBlocks.Observability.Diagnostics;

namespace Swarmcore.Hosting;

/// <summary>
/// Validates security-sensitive configuration on startup. Rejects unsafe implicit defaults
/// and detects policy conflicts. Runs before the service becomes ready.
/// </summary>
public static class StartupSecurityValidator
{
    public static void Validate(IServiceProvider serviceProvider, string serviceName)
    {
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger($"{serviceName}.SecurityValidator");
        var errors = new List<string>();

        ValidateCredentialConfiguration(serviceProvider, errors);
        ValidateSecurityHardeningOptions(serviceProvider, errors);
        ValidatePrivateTrackerPolicyConsistency(serviceProvider, errors);

        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                TrackerDiagnostics.ConfigValidationRejected.Add(1);
                logger.LogError("Security validation failed: {Error}", error);
            }

            throw new InvalidOperationException(
                $"Security validation failed for {serviceName} with {errors.Count} error(s): {string.Join("; ", errors)}");
        }

        logger.LogInformation("{ServiceName} security validation passed.", serviceName);
    }

    private static void ValidateCredentialConfiguration(IServiceProvider serviceProvider, List<string> errors)
    {
        var securityOptions = serviceProvider.GetService<IOptions<SecurityHardeningOptions>>()?.Value;
        if (securityOptions is null || !securityOptions.RequireExplicitConnectionStrings)
        {
            return;
        }

        var postgresOptions = serviceProvider.GetService<IOptions<PostgresOptions>>()?.Value;
        if (postgresOptions is not null && string.IsNullOrWhiteSpace(postgresOptions.ConnectionString))
        {
            errors.Add("PostgreSQL connection string is empty. Set Swarmcore:Postgres:ConnectionString explicitly.");
        }

        if (postgresOptions is not null && !string.IsNullOrWhiteSpace(postgresOptions.ConnectionString))
        {
            var lower = postgresOptions.ConnectionString.ToLowerInvariant();
            if (lower.Contains("password=postgres") || lower.Contains("password=password"))
            {
                errors.Add("PostgreSQL connection string uses a default/weak password. Use a strong credential.");
            }
        }

        var redisOptions = serviceProvider.GetService<IOptions<RedisOptions>>()?.Value;
        if (redisOptions is not null && string.IsNullOrWhiteSpace(redisOptions.Configuration))
        {
            errors.Add("Redis configuration is empty. Set Swarmcore:Redis:Configuration explicitly.");
        }
    }

    private static void ValidateSecurityHardeningOptions(IServiceProvider serviceProvider, List<string> errors)
    {
        var options = serviceProvider.GetService<IOptions<SecurityHardeningOptions>>()?.Value;
        if (options is null)
        {
            return;
        }

        if (options.MaxActivePasskeysPerUser < 0)
        {
            errors.Add("MaxActivePasskeysPerUser cannot be negative.");
        }

        if (options.PasskeyRevocationGracePeriodSeconds < 0)
        {
            errors.Add("PasskeyRevocationGracePeriodSeconds cannot be negative.");
        }

        if (options.DefaultPasskeyExpirationDays < 0)
        {
            errors.Add("DefaultPasskeyExpirationDays cannot be negative.");
        }
    }

    private static void ValidatePrivateTrackerPolicyConsistency(IServiceProvider serviceProvider, List<string> errors)
    {
        var securityOptions = serviceProvider.GetService<IOptions<SecurityHardeningOptions>>()?.Value;
        if (securityOptions is null || !securityOptions.EnforcePrivateTrackerPasskeyConsistency)
        {
            return;
        }

        var trackerSecurityOptions = serviceProvider.GetService<IOptions<TrackerSecurityOptions>>()?.Value;
        if (trackerSecurityOptions is null)
        {
            return;
        }

        // If passkey-in-querystring is allowed while security hardening requires rejection of
        // revoked/expired passkeys, the querystring passkey could bypass path-based sanitization.
        if (trackerSecurityOptions.AllowPasskeyInQueryString &&
            (securityOptions.RejectRevokedPasskeys || securityOptions.RejectExpiredPasskeys))
        {
            errors.Add(
                "AllowPasskeyInQueryString is enabled while passkey lifecycle enforcement is active. " +
                "This is a policy conflict — querystring passkeys bypass path-based log sanitization. " +
                "Set AllowPasskeyInQueryString=false or disable passkey lifecycle enforcement.");
        }
    }
}
