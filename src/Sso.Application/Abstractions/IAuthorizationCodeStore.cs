namespace Sso.Application.Abstractions;

public sealed record AuthorizationCodePayload(
    Guid UserId,
    Guid ClientId,
    string RedirectUri,
    string Scope,
    string? Nonce,
    string CodeChallenge);

/// <summary>
/// Authorization codes (FR-B03): Redis `oidc:code:{code}`, TTL 90 s,
/// single-use via atomic GETDEL — replay returns null.
/// </summary>
public interface IAuthorizationCodeStore
{
    Task StoreAsync(string code, AuthorizationCodePayload payload, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Atomic get-and-delete. Null = unknown or already consumed.</summary>
    Task<AuthorizationCodePayload?> ConsumeAsync(string code, CancellationToken ct = default);
}
