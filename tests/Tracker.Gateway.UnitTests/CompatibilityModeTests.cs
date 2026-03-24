using Microsoft.Extensions.Options;
using Swarmcore.BuildingBlocks.Abstractions.Options;
using Swarmcore.Contracts.Configuration;
using Tracker.Gateway.Application.Announce;
using Tracker.Gateway.Infrastructure;

namespace Tracker.Gateway.UnitTests;

public sealed class CompatibilityModeTests
{
    // ─── Strict Mode Behavior ─────────────────────────────────────────────────

    [Fact]
    public void Strict_NonCompactWhenRequired_RejectsRequest()
    {
        var validator = CreateValidator(
            requireCompact: true,
            compatibilityMode: ClientCompatibilityMode.Strict);

        var request = CreateRequest(compact: false);
        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains("compact", result.Error.FailureReason);
    }

    [Fact]
    public void Strict_NegativeCounters_RejectsRequest()
    {
        var validator = CreateValidator(compatibilityMode: ClientCompatibilityMode.Strict);

        var request = new AnnounceRequest(
            InfoHashKey.FromBytes(new byte[20]),
            PeerIdKey.FromBytes(new byte[20]),
            PeerEndpoint.FromIPv4(0x7F000001, 6881),
            -1, 0, 100, 50, true, false, TrackerEvent.Started, null, null, null);

        var result = validator.Validate(request);
        Assert.False(result.IsValid);
    }

    // ─── Standard Mode Behavior ───────────────────────────────────────────────

    [Fact]
    public void Standard_ValidRequest_Succeeds()
    {
        var validator = CreateValidator(compatibilityMode: ClientCompatibilityMode.Standard);
        var request = CreateRequest(compact: true);

        var result = validator.Validate(request);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Standard_NonCompactWhenRequired_RejectsRequest()
    {
        var validator = CreateValidator(
            requireCompact: true,
            compatibilityMode: ClientCompatibilityMode.Standard);

        var request = CreateRequest(compact: false);
        var result = validator.Validate(request);

        Assert.False(result.IsValid);
    }

    // ─── Compatibility Mode Behavior ──────────────────────────────────────────

    [Fact]
    public void Compatibility_NonCompactWhenRequired_AllowsFallback()
    {
        var validator = CreateValidator(
            requireCompact: true,
            compatibilityMode: ClientCompatibilityMode.Compatibility);

        var request = CreateRequest(compact: false);
        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Compatibility_NegativeNumWant_ClampedInsteadOfRejected()
    {
        var validator = CreateValidator(
            compatibilityMode: ClientCompatibilityMode.Compatibility,
            strictnessProfile: ProtocolStrictnessProfile.Permissive);

        var request = CreateRequest(numwant: -5);
        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    // ─── Per-Torrent Overrides ────────────────────────────────────────────────

    [Fact]
    public void PerTorrent_CompactOnlyTorrent_RejectsNonCompactInStrictMode()
    {
        var policy = CreatePolicy(compactOnly: true);
        var governance = CreateGovernance(ClientCompatibilityMode.Strict);

        var profile = EffectiveProtocolProfile.Resolve(governance, policy);

        Assert.Equal(ClientCompatibilityMode.Strict, profile.CompatibilityMode);
    }

    [Fact]
    public void PerTorrent_StrictnessProfileOverride_OverridesGlobal()
    {
        var policy = CreatePolicy(strictnessOverride: (int)ProtocolStrictnessProfile.Permissive);
        var governance = CreateGovernance(
            ClientCompatibilityMode.Standard,
            ProtocolStrictnessProfile.Strict);

        var profile = EffectiveProtocolProfile.Resolve(governance, policy);

        Assert.Equal(ProtocolStrictnessProfile.Permissive, profile.StrictnessProfile);
    }

    [Fact]
    public void PerTorrent_CompatibilityModeOverride_OverridesGlobal()
    {
        var policy = CreatePolicy(compatibilityOverride: (int)ClientCompatibilityMode.Compatibility);
        var governance = CreateGovernance(ClientCompatibilityMode.Strict);

        var profile = EffectiveProtocolProfile.Resolve(governance, policy);

        Assert.Equal(ClientCompatibilityMode.Compatibility, profile.CompatibilityMode);
    }

    [Fact]
    public void PerTorrent_NoOverrides_UsesGlobal()
    {
        var policy = CreatePolicy();
        var governance = CreateGovernance(ClientCompatibilityMode.Standard, ProtocolStrictnessProfile.Balanced);

        var profile = EffectiveProtocolProfile.Resolve(governance, policy);

        Assert.Equal(ClientCompatibilityMode.Standard, profile.CompatibilityMode);
        Assert.Equal(ProtocolStrictnessProfile.Balanced, profile.StrictnessProfile);
    }

    [Fact]
    public void PerTorrent_InvalidOverrideValue_UsesGlobal()
    {
        var policy = CreatePolicy(strictnessOverride: 999, compatibilityOverride: 999);
        var governance = CreateGovernance(ClientCompatibilityMode.Standard, ProtocolStrictnessProfile.Balanced);

        var profile = EffectiveProtocolProfile.Resolve(governance, policy);

        Assert.Equal(ClientCompatibilityMode.Standard, profile.CompatibilityMode);
        Assert.Equal(ProtocolStrictnessProfile.Balanced, profile.StrictnessProfile);
    }

    // ─── Permissive Strictness Behavior ───────────────────────────────────────

    [Fact]
    public void Permissive_NumWantExceedsHardMax_ClampedInsteadOfRejected()
    {
        var validator = CreateValidator(
            hardMaxNumWant: 100,
            strictnessProfile: ProtocolStrictnessProfile.Permissive);

        var request = CreateRequest(numwant: 150);
        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Balanced_NumWantExceedsHardMax_RejectsRequest()
    {
        var validator = CreateValidator(
            hardMaxNumWant: 100,
            strictnessProfile: ProtocolStrictnessProfile.Balanced);

        var request = CreateRequest(numwant: 150);
        var result = validator.Validate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Strict_NumWantExceedsHardMax_RejectsRequest()
    {
        var validator = CreateValidator(
            hardMaxNumWant: 100,
            strictnessProfile: ProtocolStrictnessProfile.Strict);

        var request = CreateRequest(numwant: 150);
        var result = validator.Validate(request);

        Assert.False(result.IsValid);
    }

    // ─── Scrape-Denied Torrent ────────────────────────────────────────────────

    [Fact]
    public void ScrapePolicy_AllowScrapeFalse_DeniesAccess()
    {
        var policy = CreatePolicy(allowScrape: false);
        Assert.False(policy.AllowScrape);
    }

    // ─── UDP-Disabled Torrent ─────────────────────────────────────────────────

    [Fact]
    public void TorrentPolicy_AllowUdpFalse_IndicatesUdpDenied()
    {
        var policy = CreatePolicy(allowUdp: false);
        Assert.False(policy.AllowUdp);
    }

    // ─── IPv6 Behavior Under Different Policy Modes ───────────────────────────

    [Fact]
    public void TorrentPolicy_AllowIPv6False_DeniesIPv6()
    {
        var policy = CreatePolicy(allowIPv6: false);
        Assert.False(policy.AllowIPv6);
    }

    [Fact]
    public void TorrentPolicy_AllowIPv6True_PermitsIPv6()
    {
        var policy = CreatePolicy(allowIPv6: true);
        Assert.True(policy.AllowIPv6);
    }

    // ─── Warning Message Behavior ─────────────────────────────────────────────

    [Fact]
    public void TorrentPolicy_WithWarningMessage_PropagatesToDto()
    {
        var policy = CreatePolicy(warningMessage: "test warning");
        Assert.Equal("test warning", policy.WarningMessage);
    }

    [Fact]
    public void TorrentPolicy_ModerationState_SetsExpectedValues()
    {
        var policy = CreatePolicy(moderationState: "review");
        Assert.Equal("review", policy.ModerationState);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static AnnounceRequestValidator CreateValidator(
        int hardMaxNumWant = 200,
        bool requireCompact = false,
        ClientCompatibilityMode compatibilityMode = ClientCompatibilityMode.Standard,
        ProtocolStrictnessProfile strictnessProfile = ProtocolStrictnessProfile.Balanced)
    {
        return new AnnounceRequestValidator(
            Options.Create(new TrackerSecurityOptions
            {
                HardMaxNumWant = hardMaxNumWant,
                RequireCompactResponses = requireCompact
            }),
            new RuntimeGovernanceStateService(
                Options.Create(new TrackerGovernanceOptions()),
                Options.Create(new TrackerCompatibilityOptions
                {
                    CompatibilityMode = compatibilityMode,
                    StrictnessProfile = strictnessProfile
                })));
    }

    private static AnnounceRequest CreateRequest(
        bool compact = true,
        int numwant = 50)
    {
        return new AnnounceRequest(
            InfoHashKey.FromBytes(new byte[20]),
            PeerIdKey.FromBytes(new byte[20]),
            PeerEndpoint.FromIPv4(0x7F000001, 6881),
            0, 0, 100, numwant, compact, false, TrackerEvent.Started, null, null, null);
    }

    private static TorrentPolicyDto CreatePolicy(
        bool compactOnly = true,
        bool allowScrape = true,
        bool allowUdp = true,
        bool allowIPv6 = true,
        int? strictnessOverride = null,
        int? compatibilityOverride = null,
        string? warningMessage = null,
        string? moderationState = null)
    {
        return new TorrentPolicyDto(
            "0000000000000000000000000000000000000000",
            IsPrivate: false,
            IsEnabled: true,
            AnnounceIntervalSeconds: 1800,
            MinAnnounceIntervalSeconds: 900,
            DefaultNumWant: 50,
            MaxNumWant: 200,
            AllowScrape: allowScrape,
            Version: 1,
            WarningMessage: warningMessage,
            CompactOnly: compactOnly,
            AllowUdp: allowUdp,
            AllowIPv6: allowIPv6,
            StrictnessProfileOverride: strictnessOverride,
            CompatibilityModeOverride: compatibilityOverride,
            ModerationState: moderationState);
    }

    private static IRuntimeGovernanceState CreateGovernance(
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
