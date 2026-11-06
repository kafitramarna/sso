using Sso.Domain.Entities;

namespace Sso.Application.Abstractions;

public sealed record AccessTokenResult(string Token, int ExpiresInSeconds, DateTimeOffset ExpiresAt);

/// <summary>
/// RS256 session access tokens for the SSO's own surface (/v1/*, /admin/*) —
/// KD-03. OIDC tokens for registered clients are a separate flow (Fase 2).
/// </summary>
public interface ITokenService
{
    /// <param name="sessionFamily">Refresh family this token belongs to — emitted as `fam` claim so /v1/sessions can mark "current".</param>
    AccessTokenResult CreateSessionToken(User user, Guid? sessionFamily = null);
}
