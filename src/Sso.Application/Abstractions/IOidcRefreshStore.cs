namespace Sso.Application.Abstractions;

public sealed record OidcRefreshPayload(Guid UserId, string ClientId, string Scope, Guid FamilyId);

public sealed record OidcRotateResult(RotateStatus Status, OidcRefreshPayload? Payload, string? NewToken)
{
    public static OidcRotateResult Rotated(OidcRefreshPayload payload, string newToken)
        => new(RotateStatus.Rotated, payload, newToken);
    public static readonly OidcRotateResult Invalid = new(RotateStatus.Invalid, null, null);
    public static OidcRotateResult Reuse(OidcRefreshPayload payload) => new(RotateStatus.ReuseDetected, payload, null);
}

/// <summary>
/// OIDC refresh tokens (FR-B03): opaque, hashed at rest, rotating with the same
/// reuse-detection semantics as SSO sessions — but bound to (user, client,
/// scope) and stored under the `oidc:` keyspace so they can never be
/// exchanged for an SSO session token.
/// </summary>
public interface IOidcRefreshStore
{
    Task<(string Token, Guid FamilyId)> IssueAsync(OidcRefreshPayload payload, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>ReuseDetected ⇒ family already revoked by the store.</summary>
    Task<OidcRotateResult> RotateAsync(string token, TimeSpan ttl, CancellationToken ct = default);

    Task RevokeAsync(string token, CancellationToken ct = default);

    Task RevokeFamilyAsync(Guid familyId, CancellationToken ct = default);
}
