using Sso.Domain.Enums;

namespace Sso.Domain.Entities;

/// <summary>
/// Single-use emailed token (verify email / reset password / invite). Only the
/// SHA-256 hash is stored — the raw token lives exclusively in the link (FR-A03/A04).
/// </summary>
public sealed class OneTimeToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public OneTimeTokenKind Kind { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User? User { get; set; }

    public bool IsUsable(DateTimeOffset now) => UsedAt is null && ExpiresAt > now;
}
