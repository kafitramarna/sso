namespace Sso.Application.Abstractions;

/// <summary>
/// Auth emails (KD-07): sent synchronously via SMTP adapter (Resend prod /
/// mailpit dev) or logged by ConsoleNotifier when SMTP is unconfigured.
/// </summary>
public interface INotifier
{
    Task NotifyVerifyEmailAsync(string email, string verifyUrl, CancellationToken ct = default);

    Task NotifyPasswordResetAsync(string email, string resetUrl, CancellationToken ct = default);

    Task NotifyInvitedAsync(string email, string inviteUrl, CancellationToken ct = default);
}
