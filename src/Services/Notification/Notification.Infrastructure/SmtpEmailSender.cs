using System.Diagnostics;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Notification.Application;

namespace Notification.Infrastructure;

internal sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var mimeMessage = BuildMimeMessage(message);
            using var client = new SmtpClient();

            var secureSocketOptions = DetermineSecurityOptions();

            _logger.LogDebug(
                "Connecting to SMTP server {Host}:{Port} with security {Security}",
                _options.Host, _options.Port, secureSocketOptions);

            await client.ConnectAsync(_options.Host, _options.Port, secureSocketOptions, ct);

            if (_options.RequireAuthentication)
            {
                _logger.LogDebug("Authenticating as {UserName}", _options.UserName);
                await client.AuthenticateAsync(_options.UserName, _options.Password, ct);
            }

            var response = await client.SendAsync(mimeMessage, ct);

            await client.DisconnectAsync(quit: true, ct);

            stopwatch.Stop();
            _logger.LogInformation(
                "Email sent to {Recipient} in {DurationMs}ms. SMTP response: {Response}",
                message.To, stopwatch.ElapsedMilliseconds, response);

            return new EmailSendResult(Succeeded: true, ErrorMessage: null, SmtpStatusCode: null);
        }
        catch (SmtpCommandException ex)
        {
            stopwatch.Stop();
            _logger.LogError(
                ex,
                "SMTP command error sending to {Recipient} after {DurationMs}ms. Status: {StatusCode}",
                message.To, stopwatch.ElapsedMilliseconds, (int)ex.StatusCode);

            return new EmailSendResult(
                Succeeded: false,
                ErrorMessage: ex.Message,
                SmtpStatusCode: (int)ex.StatusCode);
        }
        catch (SmtpProtocolException ex)
        {
            stopwatch.Stop();
            _logger.LogError(
                ex,
                "SMTP protocol error sending to {Recipient} after {DurationMs}ms",
                message.To, stopwatch.ElapsedMilliseconds);

            return new EmailSendResult(
                Succeeded: false,
                ErrorMessage: ex.Message,
                SmtpStatusCode: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogError(
                ex,
                "Unexpected error sending email to {Recipient} after {DurationMs}ms",
                message.To, stopwatch.ElapsedMilliseconds);

            return new EmailSendResult(
                Succeeded: false,
                ErrorMessage: ex.Message,
                SmtpStatusCode: null);
        }
    }

    private MimeMessage BuildMimeMessage(EmailMessage message)
    {
        var mimeMessage = new MimeMessage();

        mimeMessage.From.Add(new MailboxAddress(_options.SenderDisplayName, _options.SenderAddress));
        mimeMessage.To.Add(MailboxAddress.Parse(message.To));
        mimeMessage.Subject = message.Subject;

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody,
        };

        mimeMessage.Body = bodyBuilder.ToMessageBody();

        return mimeMessage;
    }

    private SecureSocketOptions DetermineSecurityOptions()
    {
        if (_options.UseSsl)
            return SecureSocketOptions.SslOnConnect;

        if (_options.UseStartTls)
            return SecureSocketOptions.StartTls;

        return SecureSocketOptions.None;
    }
}
