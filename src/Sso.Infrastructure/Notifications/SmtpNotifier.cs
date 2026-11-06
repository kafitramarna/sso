using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using Sso.Application.Abstractions;

namespace Sso.Infrastructure.Notifications;

/// <summary>
/// SMTP adapter (KD-10): Resend relay in prod (smtp.resend.com:587 +
/// RESEND_API_KEY as password), mailpit in dev. Delivery failures are logged,
/// never thrown — mail must not fail requests (KD-07).
/// </summary>
public sealed class SmtpNotifier(IConfiguration config, ILogger<SmtpNotifier> logger) : INotifier
{
    public Task NotifyVerifyEmailAsync(string email, string verifyUrl, CancellationToken ct = default)
    {
        var t = EmailTemplates.VerifyEmail(verifyUrl);
        return SendAsync(email, t.Subject, t.Body, ct);
    }

    public Task NotifyPasswordResetAsync(string email, string resetUrl, CancellationToken ct = default)
    {
        var t = EmailTemplates.PasswordReset(resetUrl);
        return SendAsync(email, t.Subject, t.Body, ct);
    }

    public Task NotifyInvitedAsync(string email, string inviteUrl, CancellationToken ct = default)
    {
        var t = EmailTemplates.Invited(inviteUrl);
        return SendAsync(email, t.Subject, t.Body, ct);
    }

    private async Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        try
        {
            var host = config["Email:Smtp:Host"];
            if (string.IsNullOrWhiteSpace(host))
            {
                logger.LogWarning("email skipped: Email:Smtp:Host not configured (to={To})", to);
                return;
            }

            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(
                config["Email:From"] ?? "SSO <no-reply@localhost>"));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;
            message.Body = new TextPart("plain") { Text = body };

            using var smtp = new SmtpClient();
            var port = config.GetValue("Email:Smtp:Port", 25);
            var secure = config.GetValue("Email:Smtp:UseStartTls", false)
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.Auto;
            await smtp.ConnectAsync(host, port, secure, ct);

            var user = config["Email:Smtp:Username"];
            if (!string.IsNullOrWhiteSpace(user))
                await smtp.AuthenticateAsync(user, config["Email:Smtp:Password"] ?? "", ct);

            await smtp.SendAsync(message, ct);
            await smtp.DisconnectAsync(quit: true, ct);
            logger.LogInformation("email sent to={To} subject={Subject}", to, subject);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "email delivery failed to={To} subject={Subject}", to, subject);
        }
    }
}
