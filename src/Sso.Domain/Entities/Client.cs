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
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}
