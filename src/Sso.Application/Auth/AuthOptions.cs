namespace Sso.Application.Auth;

/// <summary>Bound from configuration: AllowSelfRegistration + App:WebBaseUrl (KD-09).</summary>
public sealed record AuthOptions
{
    /// <summary>False in production (KD-09): /v1/auth/register → 403, invites are the only path.</summary>
    public bool AllowSelfRegistration { get; init; }

    /// <summary>Base URL of Sso.Web — email links are built from it.</summary>
    public required string WebBaseUrl { get; init; }

    public TimeSpan RefreshTokenTtl { get; init; } = TimeSpan.FromDays(7);
    public TimeSpan VerifyTokenTtl { get; init; } = TimeSpan.FromHours(24);
    public TimeSpan ResetTokenTtl { get; init; } = TimeSpan.FromHours(1);
    public TimeSpan InviteTokenTtl { get; init; } = TimeSpan.FromHours(72);
}
