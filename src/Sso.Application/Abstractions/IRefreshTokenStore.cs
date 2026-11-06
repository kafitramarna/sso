namespace Sso.Application.Abstractions;

public sealed record SessionMeta(string? UserAgent, string? Ip);

public sealed record RefreshPayload(Guid UserId, Guid FamilyId, SessionMeta Meta);

public enum RotateStatus
{
    /// <summary>Token valid — rotated, new token returned.</summary>
    Rotated,
    /// <summary>Unknown / expired token.</summary>
    Invalid,
    /// <summary>
    /// Token was already rotated once (tombstone hit) — the whole family was
    /// revoked as a probable theft (KD-03 reuse detection).
    /// </summary>
    ReuseDetected,
}

public sealed record RotateResult(RotateStatus Status, RefreshPayload? Payload, string? NewToken)
{
    public static RotateResult Rotated(RefreshPayload payload, string newToken)
        => new(RotateStatus.Rotated, payload, newToken);
    public static readonly RotateResult Invalid = new(RotateStatus.Invalid, null, null);
    public static RotateResult Reuse(RefreshPayload payload) => new(RotateStatus.ReuseDetected, payload, null);
}

/// <summary>Session as listed in GET /v1/sessions — one row per family.</summary>
public sealed record SessionInfo(
    Guid FamilyId,
    string? UserAgent,
    string? Ip,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt);

/// <summary>
/// Refresh tokens for the SSO session cookie `sso_rt` (KD-03): opaque, stored
/// hashed in Redis, rotated on every use, grouped in families (one family = one
/// signed-in session). Reuse of a rotated token revokes the family.
/// </summary>
public interface IRefreshTokenStore
{
    /// <summary>Issue a token; familyId null starts a new session family.</summary>
    Task<(string Token, Guid FamilyId)> IssueAsync(
        Guid userId, Guid? familyId, SessionMeta meta, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Validate + rotate. ReuseDetected ⇒ family already revoked by the store.</summary>
    Task<RotateResult> RotateAsync(string token, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Read payload without consuming (logout path).</summary>
    Task<RefreshPayload?> ResolveAsync(string token, CancellationToken ct = default);

    Task RevokeFamilyAsync(Guid familyId, CancellationToken ct = default);

    /// <summary>Revoke every family of the user, optionally keeping one (password change keeps current session, FR-A05).</summary>
    Task RevokeAllAsync(Guid userId, Guid? exceptFamilyId = null, CancellationToken ct = default);

    Task<IReadOnlyList<SessionInfo>> ListSessionsAsync(Guid userId, CancellationToken ct = default);
}
