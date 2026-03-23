using Microsoft.Extensions.Options;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using Swarmcore.Contracts.Configuration;
using Tracker.Gateway.Application.Announce;
using Tracker.Gateway.Infrastructure;

namespace Tracker.Gateway.UnitTests;

public sealed class GovernanceTests
{
    // ─── Maintenance Modes ────────────────────────────────────────────────────

    [Fact]
    public void GlobalMaintenanceMode_InitialState_IsFalse()
    {
        var state = CreateGovernance();
        Assert.False(state.GlobalMaintenanceMode);
    }

    [Fact]
    public void GlobalMaintenanceMode_WhenEnabled_ReturnsTrue()
    {
        var state = CreateGovernance();
        state.Apply(new RuntimeGovernanceUpdate(GlobalMaintenanceMode: true));
        Assert.True(state.GlobalMaintenanceMode);
    }

    [Fact]
    public void AnnounceDisabled_WhenEnabled_ReturnsTrue()
    {
        var state = CreateGovernance();
        state.Apply(new RuntimeGovernanceUpdate(AnnounceDisabled: true));
        Assert.True(state.AnnounceDisabled);
    }

    [Fact]
    public void ScrapeDisabled_WhenEnabled_ReturnsTrue()
    {
        var state = CreateGovernance();
        state.Apply(new RuntimeGovernanceUpdate(ScrapeDisabled: true));
        Assert.True(state.ScrapeDisabled);
    }

    [Fact]
    public void UdpDisabled_WhenEnabled_ReturnsTrue()
    {
        var state = CreateGovernance();
        state.Apply(new RuntimeGovernanceUpdate(UdpDisabled: true));
        Assert.True(state.UdpDisabled);
    }

    // ─── Read-Only Mode ───────────────────────────────────────────────────────

    [Fact]
    public void ReadOnlyMode_InitialState_IsFalse()
    {
        var state = CreateGovernance();
        Assert.False(state.ReadOnlyMode);
    }

    [Fact]
    public void ReadOnlyMode_WhenEnabled_ReturnsTrue()
    {
        var state = CreateGovernance();
        state.Apply(new RuntimeGovernanceUpdate(ReadOnlyMode: true));
        Assert.True(state.ReadOnlyMode);
    }

    // ─── Emergency Abuse Mitigation ───────────────────────────────────────────

    [Fact]
    public void EmergencyAbuseMitigation_WhenEnabled_ReturnsTrue()
    {
        var state = CreateGovernance();
        state.Apply(new RuntimeGovernanceUpdate(EmergencyAbuseMitigation: true));
        Assert.True(state.EmergencyAbuseMitigation);
    }

    // ─── IPv6 Frozen ──────────────────────────────────────────────────────────

    [Fact]
    public void IPv6Frozen_WhenEnabled_ReturnsTrue()
    {
        var state = CreateGovernance();
        state.Apply(new RuntimeGovernanceUpdate(IPv6Frozen: true));
        Assert.True(state.IPv6Frozen);
    }

    // ─── Policy Freeze ────────────────────────────────────────────────────────

    [Fact]
    public void PolicyFreezeMode_WhenEnabled_ReturnsTrue()
    {
        var state = CreateGovernance();
        state.Apply(new RuntimeGovernanceUpdate(PolicyFreezeMode: true));
        Assert.True(state.PolicyFreezeMode);
    }

    // ─── Governance Snapshot ──────────────────────────────────────────────────

    [Fact]
    public void GetSnapshot_ReflectsCurrentState()
    {
        var state = CreateGovernance();
        state.Apply(new RuntimeGovernanceUpdate(
            AnnounceDisabled: true,
            ScrapeDisabled: true,
            GlobalMaintenanceMode: true));

        var snapshot = state.GetSnapshot();

        Assert.True(snapshot.AnnounceDisabled);
        Assert.True(snapshot.ScrapeDisabled);
        Assert.True(snapshot.GlobalMaintenanceMode);
        Assert.False(snapshot.ReadOnlyMode);
        Assert.False(snapshot.EmergencyAbuseMitigation);
    }

    // ─── Partial Updates ──────────────────────────────────────────────────────

    [Fact]
    public void Apply_PartialUpdate_PreservesOtherValues()
    {
        var state = CreateGovernance();
        state.Apply(new RuntimeGovernanceUpdate(AnnounceDisabled: true));
        state.Apply(new RuntimeGovernanceUpdate(ScrapeDisabled: true));

        Assert.True(state.AnnounceDisabled);
        Assert.True(state.ScrapeDisabled);
        Assert.False(state.GlobalMaintenanceMode);
    }

    [Fact]
    public void Apply_NullValues_DoNotOverwrite()
    {
        var state = CreateGovernance();
        state.Apply(new RuntimeGovernanceUpdate(AnnounceDisabled: true));
        state.Apply(new RuntimeGovernanceUpdate(ScrapeDisabled: true));

        Assert.True(state.AnnounceDisabled); // not cleared
    }

    // ─── Compatibility Mode Updates ───────────────────────────────────────────

    [Fact]
    public void Apply_CompatibilityModeChange_UpdatesEffectiveMode()
    {
        var state = CreateGovernance();
        Assert.Equal(ClientCompatibilityMode.Standard, state.EffectiveCompatibilityMode);

        state.Apply(new RuntimeGovernanceUpdate(CompatibilityMode: ClientCompatibilityMode.Compatibility));
        Assert.Equal(ClientCompatibilityMode.Compatibility, state.EffectiveCompatibilityMode);
    }

    [Fact]
    public void Apply_StrictnessProfileChange_UpdatesEffectiveProfile()
    {
        var state = CreateGovernance();
        Assert.Equal(ProtocolStrictnessProfile.Balanced, state.EffectiveStrictnessProfile);

        state.Apply(new RuntimeGovernanceUpdate(StrictnessProfile: ProtocolStrictnessProfile.Permissive));
        Assert.Equal(ProtocolStrictnessProfile.Permissive, state.EffectiveStrictnessProfile);
    }

    // ─── Governance Transitions ───────────────────────────────────────────────

    [Fact]
    public void Transition_MaintenanceToActive_ClearsFlags()
    {
        var state = CreateGovernance();
        state.Apply(new RuntimeGovernanceUpdate(GlobalMaintenanceMode: true, AnnounceDisabled: true));
        Assert.True(state.GlobalMaintenanceMode);
        Assert.True(state.AnnounceDisabled);

        state.Apply(new RuntimeGovernanceUpdate(GlobalMaintenanceMode: false, AnnounceDisabled: false));
        Assert.False(state.GlobalMaintenanceMode);
        Assert.False(state.AnnounceDisabled);
    }

    // ─── Per-Torrent Governance Flags ─────────────────────────────────────────

    [Fact]
    public void TorrentPolicy_MaintenanceFlag_BlocksAnnounce()
    {
        var policy = new TorrentPolicyDto(
            "0000000000000000000000000000000000000000",
            false, true, 1800, 900, 50, 200, true, 1,
            MaintenanceFlag: true);

        Assert.True(policy.MaintenanceFlag);
    }

    [Fact]
    public void TorrentPolicy_TemporaryRestriction_BlocksAccess()
    {
        var policy = new TorrentPolicyDto(
            "0000000000000000000000000000000000000000",
            false, true, 1800, 900, 50, 200, true, 1,
            TemporaryRestriction: true);

        Assert.True(policy.TemporaryRestriction);
    }

    [Fact]
    public void TorrentPolicy_ModerationState_Review()
    {
        var policy = new TorrentPolicyDto(
            "0000000000000000000000000000000000000000",
            false, true, 1800, 900, 50, 200, true, 1,
            ModerationState: "review");

        Assert.Equal("review", policy.ModerationState);
    }

    // ─── Startup Config Validation ────────────────────────────────────────────

    [Fact]
    public void StartupValidation_DefaultConfig_IsValid()
    {
        var result = StartupConfigurationValidator.Validate(
            new TrackerSecurityOptions(),
            new TrackerCompatibilityOptions(),
            new TrackerGovernanceOptions(),
            new TrackerAbuseProtectionOptions(),
            new GatewayRuntimeOptions());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void StartupValidation_PasskeyInQueryString_WarnsUnsafe()
    {
        var result = StartupConfigurationValidator.Validate(
            new TrackerSecurityOptions { AllowPasskeyInQueryString = true },
            new TrackerCompatibilityOptions(),
            new TrackerGovernanceOptions(),
            new TrackerAbuseProtectionOptions(),
            new GatewayRuntimeOptions());

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, static w => w.Contains("passkey"));
    }

    [Fact]
    public void StartupValidation_PermissiveWithNoRateLimit_WarnsUnsafe()
    {
        var result = StartupConfigurationValidator.Validate(
            new TrackerSecurityOptions(),
            new TrackerCompatibilityOptions { StrictnessProfile = ProtocolStrictnessProfile.Permissive },
            new TrackerGovernanceOptions(),
            new TrackerAbuseProtectionOptions
            {
                EnableAnnounceIpRateLimit = false,
                EnableAnnouncePasskeyRateLimit = false
            },
            new GatewayRuntimeOptions());

        Assert.Contains(result.Warnings, static w => w.Contains("Permissive") && w.Contains("rate limiting"));
    }

    [Fact]
    public void StartupValidation_IPv6WithNonCompact_WarnsCompatibility()
    {
        var result = StartupConfigurationValidator.Validate(
            new TrackerSecurityOptions { AllowIPv6Peers = true, RequireCompactResponses = false },
            new TrackerCompatibilityOptions(),
            new TrackerGovernanceOptions(),
            new TrackerAbuseProtectionOptions(),
            new GatewayRuntimeOptions());

        Assert.Contains(result.Warnings, static w => w.Contains("IPv6") && w.Contains("non-compact"));
    }

    [Fact]
    public void StartupValidation_LowPeerTtl_Warns()
    {
        var result = StartupConfigurationValidator.Validate(
            new TrackerSecurityOptions(),
            new TrackerCompatibilityOptions(),
            new TrackerGovernanceOptions(),
            new TrackerAbuseProtectionOptions(),
            new GatewayRuntimeOptions { PeerTtlSeconds = 120 });

        Assert.Contains(result.Warnings, static w => w.Contains("PeerTtlSeconds"));
    }

    [Fact]
    public void StartupValidation_EmergencyWithPermissive_Warns()
    {
        var result = StartupConfigurationValidator.Validate(
            new TrackerSecurityOptions(),
            new TrackerCompatibilityOptions { StrictnessProfile = ProtocolStrictnessProfile.Permissive },
            new TrackerGovernanceOptions { EmergencyAbuseMitigation = true },
            new TrackerAbuseProtectionOptions(),
            new GatewayRuntimeOptions());

        Assert.Contains(result.Warnings, static w => w.Contains("Emergency") && w.Contains("Permissive"));
    }

    // ─── Advanced Abuse Guard ─────────────────────────────────────────────────

    [Fact]
    public void AdvancedAbuseGuard_NewIp_ReturnsNone()
    {
        var guard = new AdvancedAbuseGuard();
        Assert.Equal(AbuseRestrictionLevel.None, guard.EvaluateIp("10.0.0.1"));
    }

    [Fact]
    public void AdvancedAbuseGuard_MalformedRequests_EscalatesRestriction()
    {
        var guard = new AdvancedAbuseGuard();
        for (var i = 0; i < 20; i++)
        {
            guard.RecordMalformedRequest("10.0.0.2", null);
        }

        var level = guard.EvaluateIp("10.0.0.2");
        Assert.True(level >= AbuseRestrictionLevel.HardBlock);
    }

    [Fact]
    public void AdvancedAbuseGuard_DeniedPolicy_EscalatesRestriction()
    {
        var guard = new AdvancedAbuseGuard();
        for (var i = 0; i < 15; i++)
        {
            guard.RecordDeniedPolicy("10.0.0.3", "pk123");
        }

        var level = guard.EvaluateIp("10.0.0.3");
        Assert.True(level >= AbuseRestrictionLevel.SoftRestrict);
    }

    [Fact]
    public void AdvancedAbuseGuard_CombinedTracking_WorksCorrectly()
    {
        var guard = new AdvancedAbuseGuard();
        for (var i = 0; i < 20; i++)
        {
            guard.RecordMalformedRequest("10.0.0.4", "pk456");
        }

        var ipLevel = guard.EvaluateIp("10.0.0.4");
        var passkeyLevel = guard.EvaluatePasskey("pk456");
        var combinedLevel = guard.EvaluateCombined("10.0.0.4", "pk456");

        Assert.True(ipLevel >= AbuseRestrictionLevel.HardBlock);
        Assert.True(passkeyLevel >= AbuseRestrictionLevel.HardBlock);
        Assert.True(combinedLevel >= AbuseRestrictionLevel.HardBlock);
    }

    [Fact]
    public void AdvancedAbuseGuard_PeerIdAnomaly_IncreasesScore()
    {
        var guard = new AdvancedAbuseGuard();
        for (var i = 0; i < 5; i++)
        {
            guard.RecordPeerIdAnomaly("10.0.0.5", null);
        }

        // 5 * 4 = 20 -> SoftRestrict
        var level = guard.EvaluateIp("10.0.0.5");
        Assert.True(level >= AbuseRestrictionLevel.Warned);
    }

    [Fact]
    public void AdvancedAbuseGuard_SuspiciousPattern_IncreasesScore()
    {
        var guard = new AdvancedAbuseGuard();
        for (var i = 0; i < 5; i++)
        {
            guard.RecordSuspiciousPattern("10.0.0.6", null);
        }

        // 5 * 5 = 25 -> SoftRestrict
        var level = guard.EvaluateIp("10.0.0.6");
        Assert.Equal(AbuseRestrictionLevel.SoftRestrict, level);
    }

    [Fact]
    public void AdvancedAbuseGuard_ScrapeAmplification_TracksCorrectly()
    {
        var guard = new AdvancedAbuseGuard();
        for (var i = 0; i < 10; i++)
        {
            guard.RecordScrapeAmplification("10.0.0.7");
        }

        // 10 * 3 = 30 -> SoftRestrict
        var level = guard.EvaluateIp("10.0.0.7");
        Assert.True(level >= AbuseRestrictionLevel.SoftRestrict);
    }

    [Fact]
    public void AdvancedAbuseGuard_GetDiagnostics_ReturnsTrackedEntries()
    {
        var guard = new AdvancedAbuseGuard();
        guard.RecordMalformedRequest("10.0.0.8", null);
        guard.RecordDeniedPolicy("10.0.0.9", null);

        var diagnostics = guard.GetDiagnostics();
        Assert.True(diagnostics.Count >= 2);
    }

    [Fact]
    public void AdvancedAbuseGuard_GetSummary_ReturnsCorrectCounts()
    {
        var guard = new AdvancedAbuseGuard();
        guard.RecordMalformedRequest("10.0.0.10", null);

        var summary = guard.GetSummary();
        Assert.True(summary.TrackedIps >= 1);
    }

    // ─── Feature Toggle Correctness ───────────────────────────────────────────

    [Fact]
    public void GovernanceState_FromConfigOptions_ReflectsConfig()
    {
        var state = new RuntimeGovernanceStateService(
            Options.Create(new TrackerGovernanceOptions
            {
                GlobalMaintenanceMode = true,
                AnnounceDisabled = true,
                ReadOnlyMode = true
            }),
            Options.Create(new TrackerCompatibilityOptions
            {
                CompatibilityMode = ClientCompatibilityMode.Compatibility,
                StrictnessProfile = ProtocolStrictnessProfile.Permissive
            }));

        Assert.True(state.GlobalMaintenanceMode);
        Assert.True(state.AnnounceDisabled);
        Assert.True(state.ReadOnlyMode);
        Assert.Equal(ClientCompatibilityMode.Compatibility, state.EffectiveCompatibilityMode);
        Assert.Equal(ProtocolStrictnessProfile.Permissive, state.EffectiveStrictnessProfile);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static RuntimeGovernanceStateService CreateGovernance(
        ClientCompatibilityMode mode = ClientCompatibilityMode.Standard,
        ProtocolStrictnessProfile strictness = ProtocolStrictnessProfile.Balanced)
    {
        return new RuntimeGovernanceStateService(
            Options.Create(new TrackerGovernanceOptions()),
            Options.Create(new TrackerCompatibilityOptions
            {
                CompatibilityMode = mode,
                StrictnessProfile = strictness
            }));
    }
}
