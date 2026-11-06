using Microsoft.Extensions.Logging;
using Sso.Application.Abstractions;

namespace Sso.Infrastructure.Notifications;

/// <summary>Fallback when SMTP is not configured (KD-10): links go to the log.</summary>
public sealed class ConsoleNotifier(ILogger<ConsoleNotifier> logger) : INotifier
{
    public Task NotifyVerifyEmailAsync(string email, string verifyUrl, CancellationToken ct = default)
        => Log(email, "verify", verifyUrl);

    public Task NotifyPasswordResetAsync(string email, string resetUrl, CancellationToken ct = default)
        => Log(email, "reset", resetUrl);

    public Task NotifyInvitedAsync(string email, string inviteUrl, CancellationToken ct = default)
        => Log(email, "invite", inviteUrl);

    private Task Log(string email, string kind, string url)
    {
        logger.LogInformation("email[{Kind}] to={Email} url={Url}", kind, email, url);
        return Task.CompletedTask;
    }
}
