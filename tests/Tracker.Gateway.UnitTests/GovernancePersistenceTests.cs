using Microsoft.Extensions.Options;
using BeeTracker.BuildingBlocks.Abstractions.Options;
using Audit.Application;
using Audit.Domain;
using Tracker.Gateway.Application.Announce;
using Tracker.Gateway.Infrastructure;

namespace Tracker.Gateway.UnitTests;

public sealed class GovernancePersistenceTests
{
    // ─── Serialization Round-Trip ─────────────────────────────────────────────

    [Fact]
    public void GovernanceRedisKeys_Serialize_RoundTrips_Correctly()
    {
        var snapshot = new RuntimeGovernanceSnapshot(
            AnnounceDisabled: true,
            ScrapeDisabled: false,
            GlobalMaintenanceMode: true,
            ReadOnlyMode: false,
            EmergencyAbuseMitigation: true,
            UdpDisabled: false,
            IPv6Frozen: true,
            PolicyFreezeMode: false,
            CompatibilityMode: ClientCompatibilityMode.Compatibility,
            StrictnessProfile: ProtocolStrictnessProfile.Permissive);

        var json = GovernanceRedisKeys.Serialize(snapshot);
        var deserialized = GovernanceRedisKeys.Deserialize(json);

        Assert.NotNull(deserialized);
        Assert.Equal(snapshot.AnnounceDisabled, deserialized.AnnounceDisabled);
        Assert.Equal(snapshot.ScrapeDisabled, deserialized.ScrapeDisabled);
        Assert.Equal(snapshot.GlobalMaintenanceMode, deserialized.GlobalMaintenanceMode);
        Assert.Equal(snapshot.ReadOnlyMode, deserialized.ReadOnlyMode);
        Assert.Equal(snapshot.EmergencyAbuseMitigation, deserialized.EmergencyAbuseMitigation);
        Assert.Equal(snapshot.UdpDisabled, deserialized.UdpDisabled);
        Assert.Equal(snapshot.IPv6Frozen, deserialized.IPv6Frozen);
        Assert.Equal(snapshot.PolicyFreezeMode, deserialized.PolicyFreezeMode);
        Assert.Equal(snapshot.CompatibilityMode, deserialized.CompatibilityMode);
        Assert.Equal(snapshot.StrictnessProfile, deserialized.StrictnessProfile);
    }

    [Fact]
    public void GovernanceRedisKeys_Serialize_DefaultSnapshot_RoundTrips()
    {
        var snapshot = new RuntimeGovernanceSnapshot(
            false, false, false, false, false, false, false, false,
            ClientCompatibilityMode.Standard,
            ProtocolStrictnessProfile.Balanced);

        var json = GovernanceRedisKeys.Serialize(snapshot);
        var deserialized = GovernanceRedisKeys.Deserialize(json);

        Assert.NotNull(deserialized);
        Assert.False(deserialized.AnnounceDisabled);
        Assert.False(deserialized.GlobalMaintenanceMode);
        Assert.Equal(ClientCompatibilityMode.Standard, deserialized.CompatibilityMode);
        Assert.Equal(ProtocolStrictnessProfile.Balanced, deserialized.StrictnessProfile);
    }

    [Fact]
    public void GovernanceRedisKeys_Deserialize_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(GovernanceRedisKeys.Deserialize(null));
        Assert.Null(GovernanceRedisKeys.Deserialize(""));
        Assert.Null(GovernanceRedisKeys.Deserialize("   "));
    }

    // ─── ReplaceSnapshot ─────────────────────────────────────────────────────

    [Fact]
    public void ReplaceSnapshot_OverwritesEntireState()
    {
        var state = CreateGovernance();
        Assert.False(state.GlobalMaintenanceMode);
        Assert.False(state.AnnounceDisabled);

        var newSnapshot = new RuntimeGovernanceSnapshot(
            AnnounceDisabled: true,
            ScrapeDisabled: true,
            GlobalMaintenanceMode: true,
            ReadOnlyMode: true,
            EmergencyAbuseMitigation: true,
            UdpDisabled: true,
            IPv6Frozen: true,
            PolicyFreezeMode: true,
            CompatibilityMode: ClientCompatibilityMode.Compatibility,
            StrictnessProfile: ProtocolStrictnessProfile.Permissive);

        state.ReplaceSnapshot(newSnapshot);

        Assert.True(state.AnnounceDisabled);
        Assert.True(state.ScrapeDisabled);
        Assert.True(state.GlobalMaintenanceMode);
        Assert.True(state.ReadOnlyMode);
        Assert.True(state.EmergencyAbuseMitigation);
        Assert.True(state.UdpDisabled);
        Assert.True(state.IPv6Frozen);
        Assert.True(state.PolicyFreezeMode);
        Assert.Equal(ClientCompatibilityMode.Compatibility, state.EffectiveCompatibilityMode);
        Assert.Equal(ProtocolStrictnessProfile.Permissive, state.EffectiveStrictnessProfile);
    }

    [Fact]
    public void ReplaceSnapshot_GetSnapshot_ReturnsReplacedSnapshot()
    {
        var state = CreateGovernance();
        var newSnapshot = new RuntimeGovernanceSnapshot(
            true, false, true, false, true, false, true, false,
            ClientCompatibilityMode.Strict,
            ProtocolStrictnessProfile.Strict);

        state.ReplaceSnapshot(newSnapshot);
        var retrieved = state.GetSnapshot();

        Assert.Equal(newSnapshot, retrieved);
    }

    // ─── Apply Returns New Snapshot ──────────────────────────────────────────

    [Fact]
    public void Apply_ReturnsNewSnapshot()
    {
        var state = CreateGovernance();
        var result = state.Apply(new RuntimeGovernanceUpdate(AnnounceDisabled: true));

        Assert.True(result.AnnounceDisabled);
        Assert.Equal(state.GetSnapshot(), result);
    }

    [Fact]
    public void Apply_ReturnsSnapshotReflectingPartialUpdate()
    {
        var state = CreateGovernance();
        state.Apply(new RuntimeGovernanceUpdate(GlobalMaintenanceMode: true));

        var result = state.Apply(new RuntimeGovernanceUpdate(AnnounceDisabled: true));

        Assert.True(result.AnnounceDisabled);
        Assert.True(result.GlobalMaintenanceMode); // preserved from previous apply
    }

    // ─── GovernanceAuditService ──────────────────────────────────────────────

    [Fact]
    public void AuditGovernanceChange_WritesRecordForEachChangedField()
    {
        var writer = new TestAuditChannelWriter();
        var auditService = new GovernanceAuditService(writer);

        var before = new RuntimeGovernanceSnapshot(
            false, false, false, false, false, false, false, false,
            ClientCompatibilityMode.Standard, ProtocolStrictnessProfile.Balanced);

        var after = new RuntimeGovernanceSnapshot(
            true, true, false, false, false, false, false, false,
            ClientCompatibilityMode.Standard, ProtocolStrictnessProfile.Balanced);

        auditService.AuditGovernanceChange(before, after, "admin-1", "10.0.0.1", "TestAgent", "corr-123");

        // Expect: 1 summary + 2 field changes (AnnounceDisabled + ScrapeDisabled)
        Assert.Equal(3, writer.Records.Count);
        Assert.Contains(writer.Records, r => r.Action == AuditAction.GovernanceUpdated);
        Assert.Contains(writer.Records, r => r.Action == AuditAction.GovernanceAnnounceDisabledChanged);
        Assert.Contains(writer.Records, r => r.Action == AuditAction.GovernanceScrapeDisabledChanged);
    }

    [Fact]
    public void AuditGovernanceChange_NoFieldChanges_WritesOnlySummary()
    {
        var writer = new TestAuditChannelWriter();
        var auditService = new GovernanceAuditService(writer);

        var snapshot = new RuntimeGovernanceSnapshot(
            false, false, false, false, false, false, false, false,
            ClientCompatibilityMode.Standard, ProtocolStrictnessProfile.Balanced);

        auditService.AuditGovernanceChange(snapshot, snapshot, "admin-1", "10.0.0.1", "TestAgent", null);

        // Only the summary record (no field-level changes)
        Assert.Single(writer.Records);
        Assert.Equal(AuditAction.GovernanceUpdated, writer.Records[0].Action);
    }

    [Fact]
    public void AuditGovernanceChange_AllFieldsChanged_WritesAllRecords()
    {
        var writer = new TestAuditChannelWriter();
        var auditService = new GovernanceAuditService(writer);

        var before = new RuntimeGovernanceSnapshot(
            false, false, false, false, false, false, false, false,
            ClientCompatibilityMode.Standard, ProtocolStrictnessProfile.Balanced);

        var after = new RuntimeGovernanceSnapshot(
            true, true, true, true, true, true, true, true,
            ClientCompatibilityMode.Compatibility, ProtocolStrictnessProfile.Permissive);

        auditService.AuditGovernanceChange(before, after, "admin-1", "10.0.0.1", "TestAgent", null);

        // 1 summary + 10 field changes
        Assert.Equal(11, writer.Records.Count);
        Assert.Contains(writer.Records, r => r.Action == AuditAction.GovernanceCompatibilityModeChanged);
        Assert.Contains(writer.Records, r => r.Action == AuditAction.GovernanceStrictnessProfileChanged);
        Assert.Contains(writer.Records, r => r.Action == AuditAction.GovernanceMaintenanceModeChanged);
        Assert.Contains(writer.Records, r => r.Action == AuditAction.GovernanceEmergencyAbuseMitigationChanged);
        Assert.Contains(writer.Records, r => r.Action == AuditAction.GovernanceIPv6FrozenChanged);
        Assert.Contains(writer.Records, r => r.Action == AuditAction.GovernancePolicyFreezeModeChanged);
        Assert.Contains(writer.Records, r => r.Action == AuditAction.GovernanceUdpDisabledChanged);
        Assert.Contains(writer.Records, r => r.Action == AuditAction.GovernanceReadOnlyModeChanged);
    }

    [Fact]
    public void AuditGovernanceChange_RecordsContainActorAndContext()
    {
        var writer = new TestAuditChannelWriter();
        var auditService = new GovernanceAuditService(writer);

        var before = new RuntimeGovernanceSnapshot(
            false, false, false, false, false, false, false, false,
            ClientCompatibilityMode.Standard, ProtocolStrictnessProfile.Balanced);

        var after = new RuntimeGovernanceSnapshot(
            true, false, false, false, false, false, false, false,
            ClientCompatibilityMode.Standard, ProtocolStrictnessProfile.Balanced);

        auditService.AuditGovernanceChange(before, after, "ops-admin", "192.168.1.1", "curl/7.88", "req-abc");

        var summaryRecord = writer.Records.First(r => r.Action == AuditAction.GovernanceUpdated);
        Assert.Equal("ops-admin", summaryRecord.ActorId);
        Assert.Equal("192.168.1.1", summaryRecord.IpAddress);
        Assert.Equal("curl/7.88", summaryRecord.UserAgent);
        Assert.Equal("req-abc", summaryRecord.CorrelationId);
        Assert.Equal(AuditOutcome.Success, summaryRecord.Outcome);
    }

    [Fact]
    public void AuditGovernanceRestored_WritesRestoredRecord()
    {
        var writer = new TestAuditChannelWriter();
        var auditService = new GovernanceAuditService(writer);

        var snapshot = new RuntimeGovernanceSnapshot(
            true, false, true, false, false, false, false, false,
            ClientCompatibilityMode.Standard, ProtocolStrictnessProfile.Balanced);

        auditService.AuditGovernanceRestored(snapshot, "redis");

        Assert.Single(writer.Records);
        var record = writer.Records[0];
        Assert.Equal(AuditAction.GovernanceRestored, record.Action);
        Assert.Equal("system", record.ActorId);
        Assert.Equal(AuditOutcome.Success, record.Outcome);
        Assert.Equal("restored_from_redis", record.ReasonCode);
        Assert.NotNull(record.MetadataJson);
    }

    // ─── Audit Action Constants ──────────────────────────────────────────────

    [Fact]
    public void GovernanceAuditActions_AreDotDelimited()
    {
        var actions = new[]
        {
            AuditAction.GovernanceUpdated,
            AuditAction.GovernanceRestored,
            AuditAction.GovernanceAnnounceDisabledChanged,
            AuditAction.GovernanceScrapeDisabledChanged,
            AuditAction.GovernanceMaintenanceModeChanged,
            AuditAction.GovernanceReadOnlyModeChanged,
            AuditAction.GovernanceEmergencyAbuseMitigationChanged,
            AuditAction.GovernanceUdpDisabledChanged,
            AuditAction.GovernanceIPv6FrozenChanged,
            AuditAction.GovernancePolicyFreezeModeChanged,
            AuditAction.GovernanceCompatibilityModeChanged,
            AuditAction.GovernanceStrictnessProfileChanged,
        };

        foreach (var action in actions)
        {
            Assert.Contains('.', action);
            Assert.StartsWith("governance.", action);
        }

        // All unique
        Assert.Equal(actions.Length, actions.Distinct().Count());
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static RuntimeGovernanceStateService CreateGovernance()
    {
        return new RuntimeGovernanceStateService(
            Options.Create(new TrackerGovernanceOptions()),
            Options.Create(new TrackerCompatibilityOptions()));
    }

    private sealed class TestAuditChannelWriter : IAuditChannelWriter
    {
        public List<AuditRecord> Records { get; } = [];

        public bool TryWrite(AuditRecord record)
        {
            Records.Add(record);
            return true;
        }
    }
}
