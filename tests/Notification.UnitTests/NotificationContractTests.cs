using Notification.Application;

namespace Notification.UnitTests;

public sealed class EmailTemplateNameTests
{
    [Fact]
    public void AdminRegistration_HasExpectedValue()
    {
        Assert.Equal("admin-registration", EmailTemplateName.AdminRegistration);
    }

    [Fact]
    public void AdminActivation_HasExpectedValue()
    {
        Assert.Equal("admin-activation", EmailTemplateName.AdminActivation);
    }

    [Fact]
    public void AdminReactivation_HasExpectedValue()
    {
        Assert.Equal("admin-reactivation", EmailTemplateName.AdminReactivation);
    }

    [Fact]
    public void AdminPasswordReset_HasExpectedValue()
    {
        Assert.Equal("admin-password-reset", EmailTemplateName.AdminPasswordReset);
    }

    [Fact]
    public void AdminPasswordChanged_HasExpectedValue()
    {
        Assert.Equal("admin-password-changed", EmailTemplateName.AdminPasswordChanged);
    }

    [Fact]
    public void AdminSecurityAlert_HasExpectedValue()
    {
        Assert.Equal("admin-security-alert", EmailTemplateName.AdminSecurityAlert);
    }

    [Fact]
    public void AllTemplateNames_AreUnique()
    {
        var names = typeof(EmailTemplateName)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void AllTemplateNames_FollowKebabCaseConvention()
    {
        var names = typeof(EmailTemplateName)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        foreach (var name in names)
        {
            Assert.Matches("^[a-z][a-z0-9-]*$", name);
        }
    }
}

public sealed class SmtpOptionsTests
{
    [Fact]
    public void Defaults_HostIsLocalhost()
    {
        var options = new SmtpOptions();
        Assert.Equal("localhost", options.Host);
    }

    [Fact]
    public void Defaults_PortIs25()
    {
        var options = new SmtpOptions();
        Assert.Equal(25, options.Port);
    }

    [Fact]
    public void Defaults_SslDisabled()
    {
        var options = new SmtpOptions();
        Assert.False(options.UseSsl);
        Assert.False(options.UseStartTls);
    }

    [Fact]
    public void Defaults_AuthenticationNotRequired()
    {
        var options = new SmtpOptions();
        Assert.False(options.RequireAuthentication);
    }

    [Fact]
    public void Defaults_SenderDisplayNameIsSwarmcore()
    {
        var options = new SmtpOptions();
        Assert.Equal("Swarmcore", options.SenderDisplayName);
    }

    [Fact]
    public void SectionName_IsCorrect()
    {
        Assert.Equal("Swarmcore:Smtp", SmtpOptions.SectionName);
    }
}

public sealed class EmailMessageTests
{
    [Fact]
    public void EmailMessage_RecordProperties()
    {
        var msg = new EmailMessage("to@test.com", "Subject", "<b>Html</b>", "Text");

        Assert.Equal("to@test.com", msg.To);
        Assert.Equal("Subject", msg.Subject);
        Assert.Equal("<b>Html</b>", msg.HtmlBody);
        Assert.Equal("Text", msg.TextBody);
    }

    [Fact]
    public void EmailMessage_ValueEquality()
    {
        var msg1 = new EmailMessage("a@b.com", "S", "H", "T");
        var msg2 = new EmailMessage("a@b.com", "S", "H", "T");

        Assert.Equal(msg1, msg2);
    }
}

public sealed class EmailSendResultTests
{
    [Fact]
    public void SuccessResult_HasNoError()
    {
        var result = new EmailSendResult(true, null, 250);

        Assert.True(result.Succeeded);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(250, result.SmtpStatusCode);
    }

    [Fact]
    public void FailureResult_HasErrorDetails()
    {
        var result = new EmailSendResult(false, "Timeout", 421);

        Assert.False(result.Succeeded);
        Assert.Equal("Timeout", result.ErrorMessage);
        Assert.Equal(421, result.SmtpStatusCode);
    }
}

public sealed class EmailEnvelopeTests
{
    [Fact]
    public void EmailEnvelope_RecordProperties()
    {
        var model = new Dictionary<string, object> { ["UserName"] = "admin" };
        var envelope = new EmailEnvelope("admin@test.com", "admin-registration", model, "corr-1");

        Assert.Equal("admin@test.com", envelope.Recipient);
        Assert.Equal("admin-registration", envelope.TemplateName);
        Assert.Same(model, envelope.TemplateModel);
        Assert.Equal("corr-1", envelope.CorrelationId);
    }

    [Fact]
    public void EmailEnvelope_NullCorrelationId_IsAllowed()
    {
        var envelope = new EmailEnvelope("a@b.com", "template", new Dictionary<string, object>(), null);

        Assert.Null(envelope.CorrelationId);
    }
}

public sealed class RenderedEmailTests
{
    [Fact]
    public void RenderedEmail_RecordProperties()
    {
        var rendered = new RenderedEmail("Subject", "<p>Html</p>", "Text");

        Assert.Equal("Subject", rendered.Subject);
        Assert.Equal("<p>Html</p>", rendered.HtmlBody);
        Assert.Equal("Text", rendered.TextBody);
    }
}
