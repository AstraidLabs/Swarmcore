using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using Swarmcore.BuildingBlocks.Abstractions.Time;
using Swarmcore.BuildingBlocks.Observability.Diagnostics;
using Swarmcore.Contracts.Configuration;
using Tracker.Gateway.Application.Announce;

namespace Tracker.Gateway.Infrastructure;

/// <summary>
/// Passkey lifecycle guard that enforces revocation, expiration, and state
/// transition rules with explicit diagnostics and audit visibility.
/// Wraps around the access snapshot provider to add security-hardened checks.
/// </summary>
public sealed class PasskeyLifecycleGuard(
    IAccessSnapshotProvider innerProvider,
    IClock clock,
    IOptions<SecurityHardeningOptions> securityOptions,
    ILogger<PasskeyLifecycleGuard> logger)
{
    /// <summary>
    /// Validates a passkey against lifecycle rules (revocation, expiration).
    /// Returns the access DTO if valid, or null with a denial reason.
    /// </summary>
    public async ValueTask<PasskeyValidationResult> ValidatePasskeyAsync(string passkey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(passkey))
        {
            return PasskeyValidationResult.Denied("passkey required");
        }

        var access = await innerProvider.GetPasskeyAsync(passkey, cancellationToken);
        if (access is null)
        {
            return PasskeyValidationResult.Denied("unknown passkey");
        }

        if (access.IsRevoked)
        {
            TrackerDiagnostics.PasskeyDeniedRevoked.Add(1);
            if (securityOptions.Value.RejectRevokedPasskeys)
            {
                logger.LogInformation(
                    "Passkey denied: revoked. UserId={UserId}",
                    access.UserId);
                return PasskeyValidationResult.Denied("passkey revoked");
            }
        }

        if (access.ExpiresAtUtc is { } expiresAt && expiresAt <= clock.UtcNow)
        {
            TrackerDiagnostics.PasskeyDeniedExpired.Add(1);
            if (securityOptions.Value.RejectExpiredPasskeys)
            {
                logger.LogInformation(
                    "Passkey denied: expired at {ExpiresAt}. UserId={UserId}",
                    expiresAt,
                    access.UserId);
                return PasskeyValidationResult.Denied("passkey expired");
            }
        }

        return PasskeyValidationResult.Valid(access);
    }
}

public readonly record struct PasskeyValidationResult(bool IsValid, PasskeyAccessDto? Access, string? DenialReason)
{
    public static PasskeyValidationResult Valid(PasskeyAccessDto access) => new(true, access, null);
    public static PasskeyValidationResult Denied(string reason) => new(false, null, reason);
}
