using Audit.Domain;

namespace Audit.UnitTests;

public sealed class AuditRecordTests
{
    [Fact]
    public void Create_ValidAction_SetsAllProperties()
    {
        var record = AuditRecord.Create(
            AuditAction.AdminRegistrationRequested,
            actorId: "actor-1",
            targetUserId: "target-1",
            correlationId: "corr-123",
            ipAddress: "192.168.1.1",
            userAgent: "TestAgent/1.0",
            AuditOutcome.Success,
            reasonCode: "OK",
            metadataJson: """{"key":"value"}""");

        Assert.Equal(AuditAction.AdminRegistrationRequested, record.Action);
        Assert.Equal("actor-1", record.ActorId);
        Assert.Equal("target-1", record.TargetUserId);
        Assert.Equal("corr-123", record.CorrelationId);
        Assert.Equal("192.168.1.1", record.IpAddress);
        Assert.Equal("TestAgent/1.0", record.UserAgent);
        Assert.Equal(AuditOutcome.Success, record.Outcome);
        Assert.Equal("OK", record.ReasonCode);
        Assert.Contains("key", record.MetadataJson!);
        Assert.NotEqual(Guid.Empty, record.Id);
        Assert.True(record.OccurredAtUtc > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public void Create_NullAction_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            AuditRecord.Create(null!, null, null, null, null, null, AuditOutcome.Success));
    }

    [Fact]
    public void Create_EmptyAction_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AuditRecord.Create("", null, null, null, null, null, AuditOutcome.Success));
    }

    [Fact]
    public void Create_WhitespaceAction_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AuditRecord.Create("   ", null, null, null, null, null, AuditOutcome.Success));
    }

    [Fact]
    public void Create_NullOptionalFields_Accepted()
    {
        var record = AuditRecord.Create(
            AuditAction.AdminLoginSucceeded,
            actorId: null,
            targetUserId: null,
            correlationId: null,
            ipAddress: null,
            userAgent: null,
            AuditOutcome.Success);

        Assert.Null(record.ActorId);
        Assert.Null(record.TargetUserId);
        Assert.Null(record.CorrelationId);
        Assert.Null(record.IpAddress);
        Assert.Null(record.UserAgent);
        Assert.Null(record.ReasonCode);
        Assert.Null(record.MetadataJson);
    }

    [Fact]
    public void Create_GeneratesUniqueIds()
    {
        var ids = Enumerable.Range(0, 50)
            .Select(_ => AuditRecord.Create("test.action", null, null, null, null, null, AuditOutcome.Success).Id)
            .ToHashSet();

        Assert.Equal(50, ids.Count);
    }

    [Theory]
    [InlineData(AuditOutcome.Success)]
    [InlineData(AuditOutcome.Failure)]
    [InlineData(AuditOutcome.Denied)]
    [InlineData(AuditOutcome.Error)]
    public void Create_AllOutcomes_Accepted(AuditOutcome outcome)
    {
        var record = AuditRecord.Create("test.action", null, null, null, null, null, outcome);
        Assert.Equal(outcome, record.Outcome);
    }
}

public sealed class AuditOutcomeTests
{
    [Fact]
    public void AuditOutcome_HasExpectedValues()
    {
        Assert.Equal(0, (int)AuditOutcome.Success);
        Assert.Equal(1, (int)AuditOutcome.Failure);
        Assert.Equal(2, (int)AuditOutcome.Denied);
        Assert.Equal(3, (int)AuditOutcome.Error);
    }
}

public sealed class AuditActionTests
{
    [Fact]
    public void AuditAction_AllConstantsAreDotDelimited()
    {
        var fields = typeof(AuditAction).GetFields(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        foreach (var field in fields)
        {
            var value = (string)field.GetValue(null)!;
            Assert.Contains(".", value);
            Assert.False(string.IsNullOrWhiteSpace(value), $"{field.Name} is empty");
        }
    }

    [Fact]
    public void AuditAction_RegistrationConstants_Exist()
    {
        Assert.Equal("admin.registration.requested", AuditAction.AdminRegistrationRequested);
        Assert.Equal("admin.registration.completed", AuditAction.AdminRegistrationCompleted);
    }

    [Fact]
    public void AuditAction_ActivationConstants_Exist()
    {
        Assert.Equal("admin.activation.requested", AuditAction.AdminActivationRequested);
        Assert.Equal("admin.activation.succeeded", AuditAction.AdminActivationSucceeded);
        Assert.Equal("admin.activation.failed", AuditAction.AdminActivationFailed);
    }

    [Fact]
    public void AuditAction_PasswordConstants_Exist()
    {
        Assert.Equal("admin.password_reset.requested", AuditAction.AdminPasswordResetRequested);
        Assert.Equal("admin.password_reset.completed", AuditAction.AdminPasswordResetCompleted);
        Assert.Equal("admin.password.changed", AuditAction.AdminPasswordChanged);
    }

    [Fact]
    public void AuditAction_EmailConstants_Exist()
    {
        Assert.Equal("email.send.requested", AuditAction.EmailSendRequested);
        Assert.Equal("email.send.succeeded", AuditAction.EmailSendSucceeded);
        Assert.Equal("email.send.failed", AuditAction.EmailSendFailed);
    }

    [Fact]
    public void AuditAction_AccountLifecycleConstants_Exist()
    {
        Assert.Equal("admin.account.suspended", AuditAction.AdminAccountSuspended);
        Assert.Equal("admin.account.deactivated", AuditAction.AdminAccountDeactivated);
        Assert.Equal("admin.account.reactivation_confirmed", AuditAction.AdminAccountReactivationConfirmed);
        Assert.Equal("admin.account.locked", AuditAction.AdminAccountLocked);
    }

    [Fact]
    public void AuditAction_NoDuplicateValues()
    {
        var fields = typeof(AuditAction).GetFields(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var values = fields.Select(f => (string)f.GetValue(null)!).ToList();

        Assert.Equal(values.Count, values.Distinct().Count());
    }
}
