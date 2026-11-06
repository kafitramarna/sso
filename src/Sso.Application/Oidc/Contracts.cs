namespace Sso.Application.Oidc;

/// <summary>Validated query params of GET /oauth/authorize (FR-B02).</summary>
public sealed record AuthorizeRequest(
    string? ResponseType,
    string? ClientId,
    string? RedirectUri,
    string? Scope,
    string? State,
    string? Nonce,
    string? CodeChallenge,
    string? CodeChallengeMethod);

/// <summary>Form fields of POST /oauth/token (FR-B03).</summary>
public sealed record TokenRequest(
    string? GrantType,
    string? Code,
    string? RedirectUri,
    string? ClientId,
    string? ClientSecret,
    string? CodeVerifier,
    string? RefreshToken);

/// <summary>RFC 6749 token response shape.</summary>
public sealed record TokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    string? RefreshToken,
    string? IdToken,
    string Scope);

/// <summary>Outcome of the authorize decision — the endpoint maps it to a 302 or a 400.</summary>
public abstract record AuthorizeOutcome
{
    /// <summary>No SSO session — send the browser to Sso.Web login with return URL.</summary>
    public sealed record RedirectLogin(string Url) : AuthorizeOutcome;

    /// <summary>Client+redirect_uri already validated — errors go back as OAuth params.</summary>
    public sealed record RedirectError(string Uri, string Error, string? State) : AuthorizeOutcome;

    /// <summary>Success — redirect with ?code&state.</summary>
    public sealed record RedirectCode(string Uri, string Code, string? State) : AuthorizeOutcome;

    /// <summary>Untrusted request (unknown client/redirect_uri) — hard 400, never redirect.</summary>
    public sealed record InvalidRequest(string Error, string Description) : AuthorizeOutcome;
}

public sealed record OidcOptions
{
    public required string Issuer { get; init; }
    public required string WebBaseUrl { get; init; }
    public TimeSpan CodeTtl { get; init; } = TimeSpan.FromSeconds(90);   // FR-B03
    public TimeSpan RefreshTokenTtl { get; init; } = TimeSpan.FromDays(30);
}

// --- admin client registry (FR-C) ---

public sealed record CreateClientRequest(
    string? ClientId, string Name, string Type,
    string[] RedirectUris, string[]? AllowedScopes, bool? SkipConsent);

public sealed record UpdateClientRequest(
    string? Name, string[]? RedirectUris, string[]? AllowedScopes,
    bool? SkipConsent, bool? Active);

public sealed record ClientDto(
    Guid Id, string ClientId, string Name, string Type, bool Active,
    string[] RedirectUris, string[] AllowedScopes, bool SkipConsent,
    DateTimeOffset CreatedAt);

public sealed record CreatedClientResult(ClientDto Client, string? PlainSecret);

/// <summary>OAuth error — maps to error redirect or 400 JSON (never both).</summary>
public sealed class OidcError : Exception
{
    public string Error { get; }
    public string? Description { get; }

    public OidcError(string error, string? description = null)
        : base(description ?? error)
    {
        Error = error;
        Description = description;
    }

    public static OidcError InvalidRequest(string description) => new("invalid_request", description);
    public static OidcError InvalidClient(string description) => new("invalid_client", description);
    public static OidcError InvalidGrant(string description) => new("invalid_grant", description);
    public static OidcError UnauthorizedClient(string description) => new("unauthorized_client", description);
    public static OidcError UnsupportedGrantType() => new("unsupported_grant_type");
    public static OidcError InvalidScope(string description) => new("invalid_scope", description);
}
