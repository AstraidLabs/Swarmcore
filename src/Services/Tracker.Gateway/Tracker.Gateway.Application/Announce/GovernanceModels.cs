using BeeTracker.BuildingBlocks.Abstractions.Options;

namespace Tracker.Gateway.Application.Announce;

/// <summary>
/// Volatile in-memory runtime governance state.
/// Read on the hot path with no allocations. Updated via admin endpoints or config reload.
/// </summary>
public interface IRuntimeGovernanceState
{
    bool AnnounceDisabled { get; }
    bool ScrapeDisabled { get; }
    bool GlobalMaintenanceMode { get; }
    bool ReadOnlyMode { get; }
    bool EmergencyAbuseMitigation { get; }
    bool UdpDisabled { get; }
    bool IPv6Frozen { get; }
    bool PolicyFreezeMode { get; }

    ClientCompatibilityMode EffectiveCompatibilityMode { get; }
    ProtocolStrictnessProfile EffectiveStrictnessProfile { get; }

    RuntimeGovernanceSnapshot GetSnapshot();
    RuntimeGovernanceSnapshot Apply(RuntimeGovernanceUpdate update);
}

public sealed record RuntimeGovernanceSnapshot(
    bool AnnounceDisabled,
    bool ScrapeDisabled,
    bool GlobalMaintenanceMode,
    bool ReadOnlyMode,
    bool EmergencyAbuseMitigation,
    bool UdpDisabled,
    bool IPv6Frozen,
    bool PolicyFreezeMode,
    ClientCompatibilityMode CompatibilityMode,
    ProtocolStrictnessProfile StrictnessProfile);

public sealed record RuntimeGovernanceUpdate(
    bool? AnnounceDisabled = null,
    bool? ScrapeDisabled = null,
    bool? GlobalMaintenanceMode = null,
    bool? ReadOnlyMode = null,
    bool? EmergencyAbuseMitigation = null,
    bool? UdpDisabled = null,
    bool? IPv6Frozen = null,
    bool? PolicyFreezeMode = null,
    ClientCompatibilityMode? CompatibilityMode = null,
    ProtocolStrictnessProfile? StrictnessProfile = null);

/// <summary>
/// Resolves the effective compatibility mode and strictness profile for a request,
/// considering global settings and per-torrent overrides.
/// </summary>
public readonly record struct EffectiveProtocolProfile(
    ClientCompatibilityMode CompatibilityMode,
    ProtocolStrictnessProfile StrictnessProfile)
{
    public static EffectiveProtocolProfile Resolve(
        IRuntimeGovernanceState governance,
        BeeTracker.Contracts.Configuration.TorrentPolicyDto? policy)
    {
        var mode = governance.EffectiveCompatibilityMode;
        var strictness = governance.EffectiveStrictnessProfile;

        if (policy?.CompatibilityModeOverride is { } modeOverride &&
            Enum.IsDefined(typeof(ClientCompatibilityMode), modeOverride))
        {
            mode = (ClientCompatibilityMode)modeOverride;
        }

        if (policy?.StrictnessProfileOverride is { } strictnessOverride &&
            Enum.IsDefined(typeof(ProtocolStrictnessProfile), strictnessOverride))
        {
            strictness = (ProtocolStrictnessProfile)strictnessOverride;
        }

        return new EffectiveProtocolProfile(mode, strictness);
    }
}

/// <summary>
/// Anti-abuse intelligence: structured abuse scoring for combined IP + passkey tracking.
/// </summary>
public sealed class AbuseScore
{
    public int MalformedRequestCount { get; set; }
    public int DeniedPolicyCount { get; set; }
    public int PeerIdAnomalyCount { get; set; }
    public int SuspiciousPatternCount { get; set; }
    public int ScrapeAmplificationCount { get; set; }
    public DateTimeOffset FirstSeenUtc { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }

    public int TotalScore => MalformedRequestCount * 3 + DeniedPolicyCount * 2 +
                             PeerIdAnomalyCount * 4 + SuspiciousPatternCount * 5 +
                             ScrapeAmplificationCount * 3;

    public AbuseRestrictionLevel RestrictionLevel => TotalScore switch
    {
        >= 50 => AbuseRestrictionLevel.HardBlock,
        >= 25 => AbuseRestrictionLevel.SoftRestrict,
        >= 10 => AbuseRestrictionLevel.Warned,
        _ => AbuseRestrictionLevel.None
    };
}

public enum AbuseRestrictionLevel
{
    None = 0,
    Warned = 1,
    SoftRestrict = 2,
    HardBlock = 3
}

/// <summary>
/// Abuse diagnostics snapshot for admin visibility.
/// </summary>
public sealed record AbuseDiagnosticsEntry(
    string Key,
    string KeyType,
    int MalformedRequestCount,
    int DeniedPolicyCount,
    int PeerIdAnomalyCount,
    int SuspiciousPatternCount,
    int ScrapeAmplificationCount,
    int TotalScore,
    string RestrictionLevel,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc);
