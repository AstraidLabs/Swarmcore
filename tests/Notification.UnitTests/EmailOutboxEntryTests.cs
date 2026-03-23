using Notification.Domain;

namespace Notification.UnitTests;

public sealed class EmailOutboxEntryTests
{
    // ─── Create factory method ───────────────────────────────────────────────────

    [Fact]
    public void Create_ValidInputs_SetsAllProperties()
    {
        var entry = EmailOutboxEntry.Create(
            recipient: "admin@example.com",
            subject: "Test Subject",
            bodyHtml: "<h1>Hello</h1>",
            bodyText: "Hello",
            templateName: "admin-registration",
            correlationId: "corr-123",
            metadata: """{"key":"value"}""");

        Assert.Equal("admin@example.com", entry.Recipient);
        Assert.Equal("Test Subject", entry.Subject);
        Assert.Equal("<h1>Hello</h1>", entry.BodyHtml);
        Assert.Equal("Hello", entry.BodyText);
        Assert.Equal("admin-registration", entry.TemplateName);
        Assert.Equal("corr-123", entry.CorrelationId);
        Assert.Equal("""{"key":"value"}""", entry.MetadataJson);
        Assert.Equal(EmailOutboxStatus.Pending, entry.Status);
        Assert.Equal(0, entry.RetryCount);
        Assert.Null(entry.LastError);
        Assert.Null(entry.ScheduledAtUtc);
        Assert.Null(entry.ProcessedAtUtc);
    }

    [Fact]
    public void Create_ValidInputs_SetsCreatedAtUtcToNow()
    {
        var before = DateTime.UtcNow;
        var entry = EmailOutboxEntry.Create("a@b.com", "Subj", "<p>Hi</p>", "Hi", null, null, null);
        var after = DateTime.UtcNow;

        Assert.InRange(entry.CreatedAtUtc, before, after);
    }

    [Fact]
    public void Create_ValidInputs_GeneratesUniqueId()
    {
        var entry1 = EmailOutboxEntry.Create("a@b.com", "S1", "", "", null, null, null);
        var entry2 = EmailOutboxEntry.Create("a@b.com", "S2", "", "", null, null, null);

        Assert.NotEqual(entry1.Id, entry2.Id);
    }

    [Fact]
    public void Create_NullableFieldsNull_Succeeds()
    {
        var entry = EmailOutboxEntry.Create("a@b.com", "Subject", "", "", null, null, null);

        Assert.Null(entry.TemplateName);
        Assert.Null(entry.CorrelationId);
        Assert.Null(entry.MetadataJson);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_NullOrWhitespaceRecipient_Throws(string? recipient)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            EmailOutboxEntry.Create(recipient!, "Subject", "", "", null, null, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_NullOrWhitespaceSubject_Throws(string? subject)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            EmailOutboxEntry.Create("a@b.com", subject!, "", "", null, null, null));
    }

    // ─── MarkProcessing ──────────────────────────────────────────────────────────

    [Fact]
    public void MarkProcessing_SetsStatusToProcessing()
    {
        var entry = CreateEntry();

        entry.MarkProcessing();

        Assert.Equal(EmailOutboxStatus.Processing, entry.Status);
    }

    // ─── MarkSent ────────────────────────────────────────────────────────────────

    [Fact]
    public void MarkSent_SetsStatusToSent()
    {
        var entry = CreateEntry();

        entry.MarkSent();

        Assert.Equal(EmailOutboxStatus.Sent, entry.Status);
    }

    [Fact]
    public void MarkSent_SetsProcessedAtUtc()
    {
        var entry = CreateEntry();

        var before = DateTime.UtcNow;
        entry.MarkSent();
        var after = DateTime.UtcNow;

        Assert.NotNull(entry.ProcessedAtUtc);
        Assert.InRange(entry.ProcessedAtUtc.Value, before, after);
    }

    // ─── MarkFailed ──────────────────────────────────────────────────────────────

    [Fact]
    public void MarkFailed_SetsStatusToFailed()
    {
        var entry = CreateEntry();

        entry.MarkFailed("SMTP timeout");

        Assert.Equal(EmailOutboxStatus.Failed, entry.Status);
    }

    [Fact]
    public void MarkFailed_SetsLastError()
    {
        var entry = CreateEntry();

        entry.MarkFailed("Connection refused");

        Assert.Equal("Connection refused", entry.LastError);
    }

    [Fact]
    public void MarkFailed_IncrementsRetryCount()
    {
        var entry = CreateEntry();

        entry.MarkFailed("Error 1");
        Assert.Equal(1, entry.RetryCount);

        entry.MarkFailed("Error 2");
        Assert.Equal(2, entry.RetryCount);

        entry.MarkFailed("Error 3");
        Assert.Equal(3, entry.RetryCount);
    }

    [Fact]
    public void MarkFailed_OverwritesPreviousError()
    {
        var entry = CreateEntry();

        entry.MarkFailed("First error");
        entry.MarkFailed("Second error");

        Assert.Equal("Second error", entry.LastError);
    }

    // ─── Cancel ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Cancel_SetsStatusToCancelled()
    {
        var entry = CreateEntry();

        entry.Cancel();

        Assert.Equal(EmailOutboxStatus.Cancelled, entry.Status);
    }

    // ─── State transition sequences ──────────────────────────────────────────────

    [Fact]
    public void Lifecycle_PendingToProcessingToSent()
    {
        var entry = CreateEntry();

        Assert.Equal(EmailOutboxStatus.Pending, entry.Status);

        entry.MarkProcessing();
        Assert.Equal(EmailOutboxStatus.Processing, entry.Status);

        entry.MarkSent();
        Assert.Equal(EmailOutboxStatus.Sent, entry.Status);
        Assert.NotNull(entry.ProcessedAtUtc);
    }

    [Fact]
    public void Lifecycle_PendingToProcessingToFailedRetry()
    {
        var entry = CreateEntry();

        entry.MarkProcessing();
        entry.MarkFailed("Timeout");

        Assert.Equal(EmailOutboxStatus.Failed, entry.Status);
        Assert.Equal(1, entry.RetryCount);
        Assert.Equal("Timeout", entry.LastError);
    }

    private static EmailOutboxEntry CreateEntry()
    {
        return EmailOutboxEntry.Create(
            recipient: "test@example.com",
            subject: "Test",
            bodyHtml: "<p>Test</p>",
            bodyText: "Test",
            templateName: null,
            correlationId: null,
            metadata: null);
    }
}
