namespace Notification.Application;

public record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);

public record EmailSendResult(bool Succeeded, string? ErrorMessage, int? SmtpStatusCode);

public record RenderedEmail(string Subject, string HtmlBody, string TextBody);

public record EmailEnvelope(
    string Recipient,
    string TemplateName,
    IDictionary<string, object> TemplateModel,
    string? CorrelationId);

public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct);
}

public interface IEmailTemplateRenderer
{
    Task<RenderedEmail> RenderAsync(string templateName, IDictionary<string, object> model, CancellationToken ct);
}

public interface IEmailDispatchService
{
    Task EnqueueAsync(EmailEnvelope envelope, CancellationToken ct);
}

public interface IOutboxProcessor
{
    Task<int> ProcessPendingAsync(int batchSize, CancellationToken ct);
}

public static class EmailTemplateName
{
    public const string AdminRegistration = "admin-registration";
    public const string AdminActivation = "admin-activation";
    public const string AdminReactivation = "admin-reactivation";
    public const string AdminPasswordReset = "admin-password-reset";
    public const string AdminPasswordChanged = "admin-password-changed";
    public const string AdminSecurityAlert = "admin-security-alert";
    public const string AdminEmailChanged = "admin-email-changed";
    public const string AdminAccountLockedNotification = "admin-account-locked";
    public const string AdminAccountUnlockedNotification = "admin-account-unlocked";
    public const string AdminAccountActivatedByAdmin = "admin-account-activated-by-admin";
    public const string AdminAccountDeactivatedByAdmin = "admin-account-deactivated-by-admin";
    public const string AdminPasswordResetByAdmin = "admin-password-reset-by-admin";
    public const string AdminUserCreatedNotification = "admin-user-created";
}

public sealed class SmtpOptions
{
    public const string SectionName = "BeeTracker:Smtp";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 25;
    public bool UseSsl { get; set; }
    public bool UseStartTls { get; set; }
    public bool RequireAuthentication { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string SenderAddress { get; set; } = string.Empty;
    public string SenderDisplayName { get; set; } = "BeeTracker";
}
