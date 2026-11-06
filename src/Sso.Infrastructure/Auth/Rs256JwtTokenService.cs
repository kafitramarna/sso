using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Sso.Application.Abstractions;
using Sso.Domain.Entities;

namespace Sso.Infrastructure.Auth;

/// <summary>
/// RS256 session tokens (KD-02/KD-03). RSA key loads from `Signing:KeyPath`;
/// Development/Testing falls back to an ephemeral key (sessions die on restart,
/// which is fine). The same key later signs OIDC tokens and backs JWKS.
/// </summary>
public sealed class Rs256JwtTokenService : ITokenService
{
    public static readonly TimeSpan AccessTokenTtl = TimeSpan.FromMinutes(15);
    public const string SessionAudience = "sso";

    private readonly RSA _rsa;
    private readonly string _issuer;
    private readonly TimeSpan _ttl;

    public Rs256JwtTokenService(
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<Rs256JwtTokenService> logger)
    {
        var keyPath = configuration["Signing:KeyPath"] ?? configuration["SIGNING_KEY_PATH"];
        if (!string.IsNullOrWhiteSpace(keyPath) && File.Exists(keyPath))
        {
            _rsa = RSA.Create();
            _rsa.ImportFromPem(File.ReadAllText(keyPath));
        }
        else if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            logger.LogWarning("Signing:KeyPath not configured — using ephemeral RSA-2048 key (dev only)");
            _rsa = RSA.Create(2048);
        }
        else
        {
            throw new InvalidOperationException(
                $"Signing:KeyPath is required and must point to an RSA PEM file (got '{keyPath}').");
        }

        _issuer = configuration["Auth:Issuer"] ?? "sso";
        _ttl = TimeSpan.FromMinutes(configuration.GetValue("Auth:AccessTokenMinutes", 15));
        KeyId = Convert.ToBase64String(SHA256.HashData(_rsa.ExportSubjectPublicKeyInfo()))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=')[..16];
    }

    /// <summary>kid = base64url(SHA-256(SPKI))[..16] — stable per key (KD-02).</summary>
    public string KeyId { get; }

    /// <summary>Public key for token validation + JWKS endpoint (Fase 2).</summary>
    public RsaSecurityKey PublicKey => new(_rsa) { KeyId = KeyId };

    private SigningCredentials Credentials => new(PublicKey, SecurityAlgorithms.RsaSha256);

    public string Issuer => _issuer;

    public AccessTokenResult CreateSessionToken(User user, Guid? sessionFamily = null)
    {
        var expires = DateTime.UtcNow + _ttl;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new("email_verified", user.EmailVerified ? "true" : "false", ClaimValueTypes.Boolean),
            new("roles", "user"),
            new("auth_kind", "session"),
        };
        if (user.DisplayName is not null)
            claims.Add(new Claim("name", user.DisplayName));
        if (user.IsSsoAdmin)
            claims.Add(new Claim("roles", "sso_admin"));
        if (sessionFamily is { } fam)
            claims.Add(new Claim("fam", fam.ToString()));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expires,
            Issuer = _issuer,
            Audience = SessionAudience,
            SigningCredentials = Credentials,
        };

        var handler = new JwtSecurityTokenHandler();
        return new AccessTokenResult(
            handler.WriteToken(handler.CreateToken(descriptor)),
            (int)_ttl.TotalSeconds, expires);
    }
}
