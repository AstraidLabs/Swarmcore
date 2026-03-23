using System.Text.RegularExpressions;
using Notification.Application;

namespace Notification.Infrastructure;

internal sealed partial class InMemoryEmailTemplateRenderer : IEmailTemplateRenderer
{
    private static readonly Dictionary<string, EmailTemplateDefinition> Templates = new(StringComparer.OrdinalIgnoreCase)
    {
        [EmailTemplateName.AdminRegistration] = new(
            Subject: "Welcome to Swarmcore - Activate Your Account",
            HtmlBody: """
                <html>
                <body style="font-family: Arial, sans-serif; color: #333;">
                <h1>Welcome to Swarmcore, {{UserName}}!</h1>
                <p>Your administrator account has been created. To activate your account, use the following token:</p>
                <p style="padding: 12px; background-color: #f8f8f8; border-left: 4px solid #0066cc; font-family: monospace; font-size: 14px;">{{ActivationToken}}</p>
                <p>This token will expire in {{ExpiresInHours}} hours.</p>
                <p>If you did not request this account, please ignore this email.</p>
                <p>— The Swarmcore Team</p>
                </body>
                </html>
                """,
            TextBody: """
                Welcome to Swarmcore, {{UserName}}!

                Your administrator account has been created. To activate your account, use the following token:

                {{ActivationToken}}

                This token will expire in {{ExpiresInHours}} hours.

                If you did not request this account, please ignore this email.

                — The Swarmcore Team
                """),

        [EmailTemplateName.AdminActivation] = new(
            Subject: "Your Swarmcore Account Is Now Active",
            HtmlBody: """
                <html>
                <body style="font-family: Arial, sans-serif; color: #333;">
                <h1>Account Activated</h1>
                <p>Hello {{UserName}},</p>
                <p>Your Swarmcore administrator account has been successfully activated. You can now sign in and start managing your instance.</p>
                <p>{{Message}}</p>
                <p>If you did not activate this account, please contact your system administrator immediately.</p>
                <p>— The Swarmcore Team</p>
                </body>
                </html>
                """,
            TextBody: """
                Account Activated

                Hello {{UserName}},

                Your Swarmcore administrator account has been successfully activated. You can now sign in and start managing your instance.

                {{Message}}

                If you did not activate this account, please contact your system administrator immediately.

                — The Swarmcore Team
                """),

        [EmailTemplateName.AdminReactivation] = new(
            Subject: "Reactivate Your Swarmcore Account",
            HtmlBody: """
                <html>
                <body style="font-family: Arial, sans-serif; color: #333;">
                <h1>Reactivate Your Account</h1>
                <p>Hello {{UserName}},</p>
                <p>A request has been made to reactivate your Swarmcore administrator account. Use the following token:</p>
                <p style="padding: 12px; background-color: #f8f8f8; border-left: 4px solid #0066cc; font-family: monospace; font-size: 14px;">{{ReactivationToken}}</p>
                <p>This token will expire in {{ExpiresInHours}} hours.</p>
                <p>If you did not request reactivation, please ignore this email.</p>
                <p>— The Swarmcore Team</p>
                </body>
                </html>
                """,
            TextBody: """
                Reactivate Your Account

                Hello {{UserName}},

                A request has been made to reactivate your Swarmcore administrator account. Use the following token:

                {{ReactivationToken}}

                This token will expire in {{ExpiresInHours}} hours.

                If you did not request reactivation, please ignore this email.

                — The Swarmcore Team
                """),

        [EmailTemplateName.AdminPasswordReset] = new(
            Subject: "Swarmcore Password Reset Request",
            HtmlBody: """
                <html>
                <body style="font-family: Arial, sans-serif; color: #333;">
                <h1>Password Reset</h1>
                <p>Hello {{UserName}},</p>
                <p>We received a request to reset the password for your Swarmcore administrator account. Use the following token:</p>
                <p style="padding: 12px; background-color: #f8f8f8; border-left: 4px solid #0066cc; font-family: monospace; font-size: 14px;">{{ResetToken}}</p>
                <p>This token will expire in {{ExpiresInHours}} hours.</p>
                <p>If you did not request a password reset, no action is required. Your password will remain unchanged.</p>
                <p>— The Swarmcore Team</p>
                </body>
                </html>
                """,
            TextBody: """
                Password Reset

                Hello {{UserName}},

                We received a request to reset the password for your Swarmcore administrator account. Use the following token:

                {{ResetToken}}

                This token will expire in {{ExpiresInHours}} hours.

                If you did not request a password reset, no action is required. Your password will remain unchanged.

                — The Swarmcore Team
                """),

        [EmailTemplateName.AdminPasswordChanged] = new(
            Subject: "Your Swarmcore Password Has Been Changed",
            HtmlBody: """
                <html>
                <body style="font-family: Arial, sans-serif; color: #333;">
                <h1>Password Changed</h1>
                <p>Hello {{UserName}},</p>
                <p>{{Message}}</p>
                <p>If you made this change, no further action is required.</p>
                <p>If you did <strong>not</strong> change your password, please reset it immediately and contact your system administrator.</p>
                <p>— The Swarmcore Team</p>
                </body>
                </html>
                """,
            TextBody: """
                Password Changed

                Hello {{UserName}},

                {{Message}}

                If you made this change, no further action is required.

                If you did NOT change your password, please reset it immediately and contact your system administrator.

                — The Swarmcore Team
                """),

        [EmailTemplateName.AdminSecurityAlert] = new(
            Subject: "Swarmcore Security Alert",
            HtmlBody: """
                <html>
                <body style="font-family: Arial, sans-serif; color: #333;">
                <h1>Security Alert</h1>
                <p>Hello {{UserName}},</p>
                <p>We detected unusual activity on your Swarmcore administrator account:</p>
                <p style="padding: 12px; background-color: #f8f8f8; border-left: 4px solid #cc0000;"><strong>{{Message}}</strong></p>
                <p><strong>IP Address:</strong> {{IpAddress}}</p>
                <p>If this was you, no action is required. Otherwise, please change your password immediately and contact your system administrator.</p>
                <p>— The Swarmcore Team</p>
                </body>
                </html>
                """,
            TextBody: """
                Security Alert

                Hello {{UserName}},

                We detected unusual activity on your Swarmcore administrator account:

                {{Message}}

                IP Address: {{IpAddress}}

                If this was you, no action is required. Otherwise, please change your password immediately and contact your system administrator.

                — The Swarmcore Team
                """),
    };

    public Task<RenderedEmail> RenderAsync(string templateName, IDictionary<string, object> model, CancellationToken ct)
    {
        if (!Templates.TryGetValue(templateName, out var template))
        {
            throw new InvalidOperationException($"Email template '{templateName}' not found.");
        }

        var subject = ReplaceTokens(template.Subject, model);
        var htmlBody = ReplaceTokens(template.HtmlBody, model);
        var textBody = ReplaceTokens(template.TextBody, model);

        return Task.FromResult(new RenderedEmail(subject, htmlBody, textBody));
    }

    private static string ReplaceTokens(string text, IDictionary<string, object> model)
    {
        return TokenPattern().Replace(text, match =>
        {
            var key = match.Groups[1].Value;
            return model.TryGetValue(key, out var value)
                ? value?.ToString() ?? string.Empty
                : match.Value;
        });
    }

    [GeneratedRegex(@"\{\{(\w+)\}\}", RegexOptions.Compiled)]
    private static partial Regex TokenPattern();

    private sealed record EmailTemplateDefinition(string Subject, string HtmlBody, string TextBody);
}
