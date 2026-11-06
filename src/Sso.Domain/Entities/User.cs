using Sso.Domain.Enums;

namespace Sso.Domain.Entities;

/// <summary>Global SSO identity. `sub` claim = Id (stable, never the email — PRD §15).</summary>
public sealed class User
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public string? DisplayName { get; set; }

    /// <summary>PBKDF2 hash via PasswordHasher&lt;User&gt;; null = invited but password not set yet.</summary>
    public string? PasswordHash { get; set; }

    public DateTimeOffset? EmailVerifiedAt { get; set; }
    public UserStatus Status { get; set; } = UserStatus.Active;
    public bool IsSsoAdmin { get; set; }

    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public string? LastLoginIp { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool EmailVerified => EmailVerifiedAt is not null;
    public bool IsDisabled => Status == UserStatus.Disabled;

    public List<OneTimeToken> OneTimeTokens { get; set; } = new();
}
