using Notification.Domain;

namespace Notification.UnitTests;

public sealed class EmailDeliveryAttemptTests
{
    // ─── Record factory method ───────────────────────────────────────────────────

    [Fact]
    public void Record_SuccessfulAttempt_SetsAllProperties()
    {
        var outboxId = Guid.NewGuid();

        var attempt = EmailDeliveryAttempt.Record(
            outboxEntryId: outboxId,
            succeeded: true,
            errorMessage: null,
            smtpStatusCode: 250,
            durationMs: 142);

        Assert.Equal(outboxId, attempt.OutboxEntryId);
        Assert.True(attempt.Succeeded);
        Assert.Null(attempt.ErrorMessage);
        Assert.Equal(250, attempt.SmtpStatusCode);
        Assert.Equal(142, attempt.DurationMs);
    }

    [Fact]
    public void Record_FailedAttempt_SetsErrorDetails()
    {
        var outboxId = Guid.NewGuid();

        var attempt = EmailDeliveryAttempt.Record(
            outboxEntryId: outboxId,
            succeeded: false,
            errorMessage: "Connection refused",
            smtpStatusCode: 421,
            durationMs: 5000);

        Assert.Equal(outboxId, attempt.OutboxEntryId);
        Assert.False(attempt.Succeeded);
        Assert.Equal("Connection refused", attempt.ErrorMessage);
        Assert.Equal(421, attempt.SmtpStatusCode);
        Assert.Equal(5000, attempt.DurationMs);
    }

    [Fact]
    public void Record_SetsAttemptedAtUtcToNow()
    {
        var before = DateTime.UtcNow;
        var attempt = EmailDeliveryAttempt.Record(Guid.NewGuid(), true, null, null, 10);
        var after = DateTime.UtcNow;

        Assert.InRange(attempt.AttemptedAtUtc, before, after);
    }

    [Fact]
    public void Record_GeneratesUniqueId()
    {
        var outboxId = Guid.NewGuid();

        var attempt1 = EmailDeliveryAttempt.Record(outboxId, true, null, 250, 100);
        var attempt2 = EmailDeliveryAttempt.Record(outboxId, true, null, 250, 100);

        Assert.NotEqual(attempt1.Id, attempt2.Id);
    }

    [Fact]
    public void Record_NullSmtpStatusCode_IsAllowed()
    {
        var attempt = EmailDeliveryAttempt.Record(
            outboxEntryId: Guid.NewGuid(),
            succeeded: false,
            errorMessage: "DNS resolution failed",
            smtpStatusCode: null,
            durationMs: 30000);

        Assert.Null(attempt.SmtpStatusCode);
    }

    [Fact]
    public void Record_ZeroDuration_IsAllowed()
    {
        var attempt = EmailDeliveryAttempt.Record(Guid.NewGuid(), true, null, 250, 0);

        Assert.Equal(0, attempt.DurationMs);
    }
}
