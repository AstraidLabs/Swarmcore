using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BeeTracker.BuildingBlocks.Abstractions.Options;
using BeeTracker.BuildingBlocks.Observability.Diagnostics;
using Tracker.Gateway.Application.Announce;

namespace Tracker.Gateway.Infrastructure;

/// <summary>
/// Volatile in-memory governance state. Reads are lock-free via Volatile.Read.
/// Updates are applied atomically via snapshot replacement.
/// No Redis or database dependency on the hot path.
/// </summary>
public sealed class RuntimeGovernanceStateService : IRuntimeGovernanceState
{
    private RuntimeGovernanceSnapshot _snapshot;

    public RuntimeGovernanceStateService(IOptions<TrackerGovernanceOptions> governanceOptions, IOptions<TrackerCompatibilityOptions> compatibilityOptions)
    {
        var gov = governanceOptions.Value;
        var compat = compatibilityOptions.Value;
        _snapshot = new RuntimeGovernanceSnapshot(
            gov.AnnounceDisabled,
            gov.ScrapeDisabled,
            gov.GlobalMaintenanceMode,
            gov.ReadOnlyMode,
            gov.EmergencyAbuseMitigation,
            gov.UdpDisabled,
            gov.IPv6Frozen,
            gov.PolicyFreezeMode,
            compat.CompatibilityMode,
            compat.StrictnessProfile);
    }

    private RuntimeGovernanceSnapshot Current => Volatile.Read(ref _snapshot);

    public bool AnnounceDisabled => Current.AnnounceDisabled;
    public bool ScrapeDisabled => Current.ScrapeDisabled;
    public bool GlobalMaintenanceMode => Current.GlobalMaintenanceMode;
    public bool ReadOnlyMode => Current.ReadOnlyMode;
    public bool EmergencyAbuseMitigation => Current.EmergencyAbuseMitigation;
    public bool UdpDisabled => Current.UdpDisabled;
    public bool IPv6Frozen => Current.IPv6Frozen;
    public bool PolicyFreezeMode => Current.PolicyFreezeMode;
    public ClientCompatibilityMode EffectiveCompatibilityMode => Current.CompatibilityMode;
    public ProtocolStrictnessProfile EffectiveStrictnessProfile => Current.StrictnessProfile;

    public RuntimeGovernanceSnapshot GetSnapshot() => Current;

    public void Apply(RuntimeGovernanceUpdate update)
    {
        var current = Current;
        var next = new RuntimeGovernanceSnapshot(
            update.AnnounceDisabled ?? current.AnnounceDisabled,
            update.ScrapeDisabled ?? current.ScrapeDisabled,
            update.GlobalMaintenanceMode ?? current.GlobalMaintenanceMode,
            update.ReadOnlyMode ?? current.ReadOnlyMode,
            update.EmergencyAbuseMitigation ?? current.EmergencyAbuseMitigation,
            update.UdpDisabled ?? current.UdpDisabled,
            update.IPv6Frozen ?? current.IPv6Frozen,
            update.PolicyFreezeMode ?? current.PolicyFreezeMode,
            update.CompatibilityMode ?? current.CompatibilityMode,
            update.StrictnessProfile ?? current.StrictnessProfile);
        Volatile.Write(ref _snapshot, next);
    }
}

/// <summary>
/// Advanced abuse guard with combined IP + passkey scoring, anomaly detection,
/// and structured abuse diagnostics. Extends basic rate limiting with behavioral analysis.
/// </summary>
public sealed class AdvancedAbuseGuard
{
    private readonly ConcurrentDictionary<string, AbuseScore> _ipScores = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AbuseScore> _passkeyScores = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AbuseScore> _combinedScores = new(StringComparer.Ordinal);
    private long _lastSweepWindow;

    public void RecordMalformedRequest(string ip, string? passkey)
    {
        var now = DateTimeOffset.UtcNow;
        GetOrCreateScore(_ipScores, ip, now).MalformedRequestCount++;
        TrackerDiagnostics.AbuseIntelMalformed.Add(1);
        if (!string.IsNullOrWhiteSpace(passkey))
        {
            GetOrCreateScore(_passkeyScores, passkey, now).MalformedRequestCount++;
            GetOrCreateScore(_combinedScores, $"{ip}|{passkey}", now).MalformedRequestCount++;
        }
    }

    public void RecordDeniedPolicy(string ip, string? passkey)
    {
        var now = DateTimeOffset.UtcNow;
        GetOrCreateScore(_ipScores, ip, now).DeniedPolicyCount++;
        TrackerDiagnostics.AbuseIntelDenied.Add(1);
        if (!string.IsNullOrWhiteSpace(passkey))
        {
            GetOrCreateScore(_passkeyScores, passkey, now).DeniedPolicyCount++;
            GetOrCreateScore(_combinedScores, $"{ip}|{passkey}", now).DeniedPolicyCount++;
        }
    }

    public void RecordPeerIdAnomaly(string ip, string? passkey)
    {
        var now = DateTimeOffset.UtcNow;
        GetOrCreateScore(_ipScores, ip, now).PeerIdAnomalyCount++;
        TrackerDiagnostics.AbuseIntelPeerIdAnomaly.Add(1);
        if (!string.IsNullOrWhiteSpace(passkey))
        {
            GetOrCreateScore(_passkeyScores, passkey, now).PeerIdAnomalyCount++;
        }
    }

    public void RecordSuspiciousPattern(string ip, string? passkey)
    {
        var now = DateTimeOffset.UtcNow;
        GetOrCreateScore(_ipScores, ip, now).SuspiciousPatternCount++;
        TrackerDiagnostics.AbuseIntelSuspicious.Add(1);
        if (!string.IsNullOrWhiteSpace(passkey))
        {
            GetOrCreateScore(_passkeyScores, passkey, now).SuspiciousPatternCount++;
        }
    }

    public void RecordScrapeAmplification(string ip)
    {
        var now = DateTimeOffset.UtcNow;
        GetOrCreateScore(_ipScores, ip, now).ScrapeAmplificationCount++;
        TrackerDiagnostics.AbuseIntelScrapeAmplification.Add(1);
    }

    public AbuseRestrictionLevel EvaluateIp(string ip)
    {
        MaybeSweep();
        return _ipScores.TryGetValue(ip, out var score) ? score.RestrictionLevel : AbuseRestrictionLevel.None;
    }

    public AbuseRestrictionLevel EvaluatePasskey(string passkey)
    {
        return _passkeyScores.TryGetValue(passkey, out var score) ? score.RestrictionLevel : AbuseRestrictionLevel.None;
    }

    public AbuseRestrictionLevel EvaluateCombined(string ip, string passkey)
    {
        var key = $"{ip}|{passkey}";
        return _combinedScores.TryGetValue(key, out var score) ? score.RestrictionLevel : AbuseRestrictionLevel.None;
    }

    public IReadOnlyList<AbuseDiagnosticsEntry> GetDiagnostics(int maxEntries = 100)
    {
        var entries = new List<AbuseDiagnosticsEntry>();

        foreach (var (key, score) in _ipScores)
        {
            if (score.TotalScore > 0)
            {
                entries.Add(new AbuseDiagnosticsEntry(key, "ip",
                    score.MalformedRequestCount, score.DeniedPolicyCount,
                    score.PeerIdAnomalyCount, score.SuspiciousPatternCount,
                    score.ScrapeAmplificationCount, score.TotalScore,
                    score.RestrictionLevel.ToString(), score.FirstSeenUtc, score.LastSeenUtc));
            }
        }

        foreach (var (key, score) in _passkeyScores)
        {
            if (score.TotalScore > 0)
            {
                entries.Add(new AbuseDiagnosticsEntry(key, "passkey",
                    score.MalformedRequestCount, score.DeniedPolicyCount,
                    score.PeerIdAnomalyCount, score.SuspiciousPatternCount,
                    score.ScrapeAmplificationCount, score.TotalScore,
                    score.RestrictionLevel.ToString(), score.FirstSeenUtc, score.LastSeenUtc));
            }
        }

        return entries
            .OrderByDescending(static e => e.TotalScore)
            .Take(maxEntries)
            .ToList();
    }

    public AbuseDiagnosticsSummary GetSummary()
    {
        var ipCount = 0;
        var passkeyCount = 0;
        var warned = 0;
        var softRestricted = 0;
        var hardBlocked = 0;

        foreach (var (_, score) in _ipScores)
        {
            if (score.TotalScore <= 0) continue;
            ipCount++;
            switch (score.RestrictionLevel)
            {
                case AbuseRestrictionLevel.Warned: warned++; break;
                case AbuseRestrictionLevel.SoftRestrict: softRestricted++; break;
                case AbuseRestrictionLevel.HardBlock: hardBlocked++; break;
            }
        }

        foreach (var (_, score) in _passkeyScores)
        {
            if (score.TotalScore > 0) passkeyCount++;
        }

        return new AbuseDiagnosticsSummary(ipCount, passkeyCount, warned, softRestricted, hardBlocked);
    }

    private void MaybeSweep()
    {
        var nowWindow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (nowWindow - Volatile.Read(ref _lastSweepWindow) < 300) return;
        Volatile.Write(ref _lastSweepWindow, nowWindow);

        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-30);
        SweepDictionary(_ipScores, cutoff);
        SweepDictionary(_passkeyScores, cutoff);
        SweepDictionary(_combinedScores, cutoff);
    }

    private static void SweepDictionary(ConcurrentDictionary<string, AbuseScore> dict, DateTimeOffset cutoff)
    {
        foreach (var (key, score) in dict)
        {
            if (score.LastSeenUtc < cutoff)
            {
                dict.TryRemove(key, out _);
            }
        }
    }

    private static AbuseScore GetOrCreateScore(ConcurrentDictionary<string, AbuseScore> dict, string key, DateTimeOffset now)
    {
        var score = dict.GetOrAdd(key, static _ => new AbuseScore());
        if (score.FirstSeenUtc == default) score.FirstSeenUtc = now;
        score.LastSeenUtc = now;
        return score;
    }
}

public sealed record AbuseDiagnosticsSummary(
    int TrackedIps,
    int TrackedPasskeys,
    int WarnedCount,
    int SoftRestrictedCount,
    int HardBlockedCount);

/// <summary>
/// Startup configuration validator. Checks for dangerous or incompatible settings.
/// </summary>
public static class StartupConfigurationValidator
{
    public sealed record ConfigValidationResult(bool IsValid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

    public static ConfigValidationResult Validate(
        TrackerSecurityOptions security,
        TrackerCompatibilityOptions compatibility,
        TrackerGovernanceOptions governance,
        TrackerAbuseProtectionOptions abuseProtection,
        GatewayRuntimeOptions runtime)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // Incompatible compact settings
        if (!security.RequireCompactResponses && compatibility.CompatibilityMode == ClientCompatibilityMode.Strict)
        {
            warnings.Add("Strict compatibility mode with RequireCompactResponses=false may cause unexpected behavior with strict clients.");
        }

        // Unsafe scrape exposure
        if (security.AllowPasskeyInQueryString)
        {
            warnings.Add("AllowPasskeyInQueryString=true exposes passkeys in URLs and proxy logs. Consider using path-based passkey routing.");
        }

        // Permissive mode without abuse protection
        if (compatibility.StrictnessProfile == ProtocolStrictnessProfile.Permissive &&
            !abuseProtection.EnableAnnounceIpRateLimit && !abuseProtection.EnableAnnouncePasskeyRateLimit)
        {
            warnings.Add("Permissive strictness with no rate limiting enabled is unsafe for production.");
        }

        // IPv6 without compact
        if (security.AllowIPv6Peers && !security.RequireCompactResponses)
        {
            warnings.Add("IPv6 peers with non-compact responses may cause compatibility issues with older clients.");
        }

        // Global maintenance with announce enabled
        if (governance.GlobalMaintenanceMode && !governance.AnnounceDisabled)
        {
            warnings.Add("GlobalMaintenanceMode is set but AnnounceDisabled is false. Maintenance mode will override announce availability.");
        }

        // Emergency abuse mitigation with permissive profile
        if (governance.EmergencyAbuseMitigation && compatibility.StrictnessProfile == ProtocolStrictnessProfile.Permissive)
        {
            warnings.Add("EmergencyAbuseMitigation with Permissive strictness reduces effectiveness of abuse mitigation.");
        }

        // Peer TTL too low
        if (runtime.PeerTtlSeconds < 300)
        {
            warnings.Add($"PeerTtlSeconds={runtime.PeerTtlSeconds} is very aggressive. Peers may be expired before re-announcing.");
        }

        // Hard max numwant vs max peers per response mismatch
        if (security.HardMaxNumWant > runtime.MaxPeersPerResponse * 3)
        {
            warnings.Add($"HardMaxNumWant={security.HardMaxNumWant} is much larger than MaxPeersPerResponse={runtime.MaxPeersPerResponse}. Clients may request more peers than will be returned.");
        }

        // Private/public policy overlap warning
        if (security.AllowPasskeyInQueryString && !security.RequireCompactResponses)
        {
            warnings.Add("Combination of passkey-in-querystring and non-compact responses creates risk of passkey leakage in non-compact peer dictionaries.");
        }

        // Client IP override is a security-sensitive feature
        if (security.AllowClientIpOverride)
        {
            warnings.Add("AllowClientIpOverride=true allows clients to specify their own IP address via the 'ip' query parameter. This is a security risk unless the tracker operates behind a trusted proxy that does not preserve client IPs.");
        }

        return new ConfigValidationResult(errors.Count == 0, errors, warnings);
    }
}
