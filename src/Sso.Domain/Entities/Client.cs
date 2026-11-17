using Sso.Domain.Enums;

namespace Sso.Domain.Entities;

/// <summary>Registered OIDC client (FR-C). Secret is stored hashed, shown once at creation.</summary>
public sealed class Client
{
    public Guid Id { get; set; }
    public required string ClientId { get; set; }
    public required string Name { get; set; }
    public ClientType Type { get; set; }
    public string? SecretHash { get; set; }
    public List<string> RedirectUris { get; set; } = new();
    public List<string> AllowedScopes { get; set; } = new();
    public bool SkipConsent { get; set; }

    /// <summary>Short blurb shown on the /dashboard launcher card.</summary>
    public string? Description { get; set; }

    /// <summary>Where the launcher sends the user. Null = client is OIDC-only, not an app.</summary>
    public string? LaunchUrl { get; set; }

    /// <summary>KD-14: when true, /oauth/authorize denies users without a client_assignment row.</summary>
    public bool RequireAssignment { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}
