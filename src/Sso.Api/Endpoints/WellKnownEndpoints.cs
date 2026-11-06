using Sso.Infrastructure.Auth;

namespace Sso.Api.Endpoints;

/// <summary>
/// OIDC discovery + JWKS (FR-B01/B05, KD-02): public, cacheable metadata so
/// every VenusVerse service can validate RS256 tokens without shared secrets.
/// </summary>
public static class WellKnownEndpoints
{
    public static void MapWellKnownEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/.well-known/openid-configuration", (Rs256JwtTokenService jwt) =>
            {
                var iss = jwt.Issuer.TrimEnd('/');
                return Results.Json(new
                {
                    issuer = iss,
                    authorization_endpoint = $"{iss}/oauth/authorize",
                    token_endpoint = $"{iss}/oauth/token",
                    userinfo_endpoint = $"{iss}/oauth/userinfo",
                    jwks_uri = $"{iss}/.well-known/jwks.json",
                    revocation_endpoint = $"{iss}/oauth/revoke",
                    response_types_supported = new[] { "code" },
                    grant_types_supported = new[] { "authorization_code", "refresh_token" },
                    subject_types_supported = new[] { "public" },
                    id_token_signing_alg_values_supported = new[] { "RS256" },
                    scopes_supported = new[] { "openid", "profile", "email", "offline_access" },
                    token_endpoint_auth_methods_supported
                        = new[] { "client_secret_basic", "client_secret_post" },
                    code_challenge_methods_supported = new[] { "S256" },
                    claims_supported = new[]
                    {
                        "sub", "iss", "aud", "exp", "iat", "jti",
                        "email", "email_verified", "name", "nonce",
                    },
                });
            })
            .AllowAnonymous();

        routes.MapGet("/.well-known/jwks.json", (Rs256JwtTokenService jwt) =>
            {
                var p = jwt.PublicKeyParameters;
                return Results.Json(new
                {
                    keys = new[]
                    {
                        new
                        {
                            kty = "RSA",
                            use = "sig",
                            kid = jwt.KeyId,
                            alg = "RS256",
                            n = Base64Url(p.Modulus!),
                            e = Base64Url(p.Exponent!),
                        },
                    },
                });
            })
            .AllowAnonymous();
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
