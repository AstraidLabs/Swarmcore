using Notification.Application;
using Notification.Infrastructure;

namespace Notification.UnitTests;

public sealed class EmailTemplateRendererTests
{
    private readonly InMemoryEmailTemplateRenderer _renderer = new();

    // ─── Admin Registration template ─────────────────────────────────────────────

    [Fact]
    public async Task RenderAsync_AdminRegistration_ReplacesAllTokens()
    {
        var model = new Dictionary<string, object>
        {
            ["UserName"] = "admin",
            ["ActivationToken"] = "tok-abc-123",
            ["ExpiresInHours"] = 24,
        };

        var result = await _renderer.RenderAsync(EmailTemplateName.AdminRegistration, model, CancellationToken.None);

        Assert.Contains("BeeTracker", result.Subject);
        Assert.Contains("admin", result.HtmlBody);
        Assert.Contains("tok-abc-123", result.HtmlBody);
        Assert.Contains("24", result.HtmlBody);
        Assert.Contains("admin", result.TextBody);
        Assert.Contains("tok-abc-123", result.TextBody);
        Assert.Contains("24", result.TextBody);
    }

    [Fact]
    public async Task RenderAsync_AdminRegistration_SubjectIsCorrect()
    {
        var model = new Dictionary<string, object> { ["UserName"] = "testuser" };

        var result = await _renderer.RenderAsync(EmailTemplateName.AdminRegistration, model, CancellationToken.None);

        Assert.Equal("Welcome to BeeTracker - Activate Your Account", result.Subject);
    }

    // ─── Admin Activation template ───────────────────────────────────────────────

    [Fact]
    public async Task RenderAsync_AdminActivation_ReplacesTokens()
    {
        var model = new Dictionary<string, object>
        {
            ["UserName"] = "activateduser",
            ["Message"] = "Welcome aboard!",
        };

        var result = await _renderer.RenderAsync(EmailTemplateName.AdminActivation, model, CancellationToken.None);

        Assert.Contains("activateduser", result.HtmlBody);
        Assert.Contains("Welcome aboard!", result.HtmlBody);
        Assert.Contains("activateduser", result.TextBody);
        Assert.Contains("Welcome aboard!", result.TextBody);
    }

    // ─── Admin Reactivation template ─────────────────────────────────────────────

    [Fact]
    public async Task RenderAsync_AdminReactivation_ReplacesTokens()
    {
        var model = new Dictionary<string, object>
        {
            ["UserName"] = "reactivateduser",
            ["ReactivationToken"] = "react-tok-456",
            ["ExpiresInHours"] = 48,
        };

        var result = await _renderer.RenderAsync(EmailTemplateName.AdminReactivation, model, CancellationToken.None);

        Assert.Contains("reactivateduser", result.HtmlBody);
        Assert.Contains("react-tok-456", result.HtmlBody);
        Assert.Contains("48", result.HtmlBody);
    }

    // ─── Admin Password Reset template ───────────────────────────────────────────

    [Fact]
    public async Task RenderAsync_AdminPasswordReset_ReplacesTokens()
    {
        var model = new Dictionary<string, object>
        {
            ["UserName"] = "resetuser",
            ["ResetToken"] = "reset-tok-789",
            ["ExpiresInHours"] = 1,
        };

        var result = await _renderer.RenderAsync(EmailTemplateName.AdminPasswordReset, model, CancellationToken.None);

        Assert.Contains("resetuser", result.HtmlBody);
        Assert.Contains("reset-tok-789", result.HtmlBody);
        Assert.Contains("1", result.HtmlBody);
        Assert.Contains("Password Reset", result.Subject);
    }

    // ─── Admin Password Changed template ─────────────────────────────────────────

    [Fact]
    public async Task RenderAsync_AdminPasswordChanged_ReplacesTokens()
    {
        var model = new Dictionary<string, object>
        {
            ["UserName"] = "changeduser",
            ["Message"] = "Your password was updated successfully.",
        };

        var result = await _renderer.RenderAsync(EmailTemplateName.AdminPasswordChanged, model, CancellationToken.None);

        Assert.Contains("changeduser", result.HtmlBody);
        Assert.Contains("Your password was updated successfully.", result.HtmlBody);
        Assert.Contains("Password", result.Subject);
        Assert.Contains("Changed", result.Subject);
    }

    // ─── Admin Security Alert template ───────────────────────────────────────────

    [Fact]
    public async Task RenderAsync_AdminSecurityAlert_ReplacesAllTokens()
    {
        var model = new Dictionary<string, object>
        {
            ["UserName"] = "alerteduser",
            ["Message"] = "Multiple failed login attempts detected.",
            ["IpAddress"] = "192.168.1.100",
        };

        var result = await _renderer.RenderAsync(EmailTemplateName.AdminSecurityAlert, model, CancellationToken.None);

        Assert.Contains("alerteduser", result.HtmlBody);
        Assert.Contains("Multiple failed login attempts detected.", result.HtmlBody);
        Assert.Contains("192.168.1.100", result.HtmlBody);
        Assert.Contains("Security Alert", result.Subject);
        Assert.Contains("192.168.1.100", result.TextBody);
    }

    // ─── Token replacement behavior ──────────────────────────────────────────────

    [Fact]
    public async Task RenderAsync_MissingModelKey_LeavesTokenUnreplaced()
    {
        var model = new Dictionary<string, object>
        {
            ["UserName"] = "admin",
            // ActivationToken intentionally missing
        };

        var result = await _renderer.RenderAsync(EmailTemplateName.AdminRegistration, model, CancellationToken.None);

        Assert.Contains("admin", result.HtmlBody);
        Assert.Contains("{{ActivationToken}}", result.HtmlBody);
    }

    [Fact]
    public async Task RenderAsync_EmptyModel_LeavesAllTokensUnreplaced()
    {
        var model = new Dictionary<string, object>();

        var result = await _renderer.RenderAsync(EmailTemplateName.AdminRegistration, model, CancellationToken.None);

        Assert.Contains("{{UserName}}", result.HtmlBody);
        Assert.Contains("{{ActivationToken}}", result.HtmlBody);
    }

    [Fact]
    public async Task RenderAsync_NullModelValue_ReplacesWithEmpty()
    {
        var model = new Dictionary<string, object>
        {
            ["UserName"] = null!,
            ["Message"] = "test",
        };

        var result = await _renderer.RenderAsync(EmailTemplateName.AdminActivation, model, CancellationToken.None);

        Assert.DoesNotContain("{{UserName}}", result.HtmlBody);
    }

    // ─── Template lookup ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RenderAsync_UnknownTemplate_ThrowsInvalidOperation()
    {
        var model = new Dictionary<string, object>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _renderer.RenderAsync("nonexistent-template", model, CancellationToken.None));
    }

    [Fact]
    public async Task RenderAsync_TemplateLookupIsCaseInsensitive()
    {
        var model = new Dictionary<string, object> { ["UserName"] = "admin" };

        var result = await _renderer.RenderAsync("ADMIN-REGISTRATION", model, CancellationToken.None);

        Assert.Contains("admin", result.HtmlBody);
    }

    // ─── All templates produce non-empty output ──────────────────────────────────

    [Theory]
    [InlineData("admin-registration")]
    [InlineData("admin-activation")]
    [InlineData("admin-reactivation")]
    [InlineData("admin-password-reset")]
    [InlineData("admin-password-changed")]
    [InlineData("admin-security-alert")]
    public async Task RenderAsync_AllTemplates_ProduceNonEmptyOutput(string templateName)
    {
        var model = new Dictionary<string, object>
        {
            ["UserName"] = "user",
            ["ActivationToken"] = "tok",
            ["ReactivationToken"] = "tok",
            ["ResetToken"] = "tok",
            ["ExpiresInHours"] = 24,
            ["Message"] = "msg",
            ["IpAddress"] = "1.2.3.4",
        };

        var result = await _renderer.RenderAsync(templateName, model, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(result.Subject));
        Assert.False(string.IsNullOrWhiteSpace(result.HtmlBody));
        Assert.False(string.IsNullOrWhiteSpace(result.TextBody));
    }
}
