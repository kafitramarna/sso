using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Sso.Application.Abstractions;
using Sso.Domain.Entities;
using Sso.Domain.Enums;
using static Sso.Application.Oidc.AuthorizeOutcome;

namespace Sso.Application.Oidc;

/// <summary>
/// OIDC provider layer (KD-01): authorization code + PKCE only — never
/// implicit, never password grant. Untrusted inputs (client_id, redirect_uri)
/// fail as hard errors without redirect; only after both are validated may an
/// error be sent back through the registered redirect_uri.
/// </summary>
public sealed class OidcService(
    ISsoDbContext db,
    IAuthorizationCodeStore codes,
    IOidcRefreshStore refreshTokens,
    ITokenService tokens,
    IAuditWriter audit,
    OidcOptions options)
{
    // --- GET /oauth/authorize (FR-B02) ---

    public async Task<AuthorizeOutcome> DecideAuthorizeAsync(
        AuthorizeRequest req, Guid? userId, string returnUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.ClientId))
            return new InvalidRequest("invalid_request", "client_id is required");

        var client = await db.Clients.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ClientId == req.ClientId.Trim(), ct);
        if (client is null || !client.Active)
            return new InvalidRequest("invalid_client", "unknown or inactive client");

        // Exact-match only — arbitrary redirect URIs are never honored (PRD §15).
        if (string.IsNullOrWhiteSpace(req.RedirectUri)
            || !client.RedirectUris.Contains(req.RedirectUri))
            return new InvalidRequest("invalid_request", "redirect_uri not registered");

        var uri = req.RedirectUri;
        RedirectError Err(string error, string description)
            => new(AppendQuery(uri,
                    ("error", error), ("error_description", description), ("state", req.State)),
                error, req.State);

        if (req.ResponseType != "code")
            return Err("unsupported_response_type", "only response_type=code is supported");

        var scopes = (req.Scope ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!scopes.Contains("openid"))
            return Err("invalid_scope", "scope must include openid");
        if (scopes.Any(s => !client.AllowedScopes.Contains(s)))
            return Err("invalid_scope", "requested scope exceeds client registration");

        // PKCE is mandatory for every client (KD-01).
        if (req.CodeChallengeMethod != "S256" || string.IsNullOrWhiteSpace(req.CodeChallenge))
            return Err("invalid_request", "PKCE with S256 code_challenge is required");

        if (!client.SkipConsent)
            return Err("consent_required", "explicit consent UI is not implemented in v1"); // KD-04

        if (userId is null)
            return new RedirectLogin(
                $"{options.WebBaseUrl.TrimEnd('/')}/login?return={Uri.EscapeDataString(returnUrl)}");

        // KD-14 — an admin-assigned app gate: without a client_assignment row
        // (and without sso_admin) the user is sent back with access_denied.
        if (client.RequireAssignment)
        {
            var granted = await db.Users.AsNoTracking()
                .Where(u => u.Id == userId.Value)
                .Select(u => u.IsSsoAdmin
                    || db.ClientAssignments.Any(a => a.UserId == u.Id && a.ClientId == client.Id))
                .FirstOrDefaultAsync(ct);
            if (!granted)
            {
                await audit.WriteAsync(AuditEvents.OidcAuthorizeDenied, userId, client.Id,
                    metadata: new { reason = "assignment_required" }, ct: ct);
                return Err("access_denied", "your account has not been granted this application");
            }
        }

        var code = Base64Url(RandomNumberGenerator.GetBytes(32));
        await codes.StoreAsync(code, new AuthorizationCodePayload(
            userId.Value, client.Id, uri, string.Join(' ', scopes), req.Nonce, req.CodeChallenge),
            options.CodeTtl, ct);
        await audit.WriteAsync(AuditEvents.OidcAuthorize, userId, client.Id,
            metadata: new { scope = string.Join(' ', scopes) }, ct: ct);

        return new RedirectCode(
            AppendQuery(uri, ("code", code), ("state", req.State)), code, req.State);
    }

    // --- POST /oauth/token (FR-B03) ---

    public async Task<TokenResponse> ExchangeTokenAsync(
        TokenRequest req, IPAddress? ip, string? userAgent, CancellationToken ct = default)
    {
        var client = await AuthenticateClientAsync(req, ct);
        try
        {
            return req.GrantType switch
            {
                "authorization_code" => await GrantCodeAsync(client, req, ip, userAgent, ct),
                "refresh_token" => await GrantRefreshAsync(client, req, ip, userAgent, ct),
                _ => throw OidcError.UnsupportedGrantType(),
            };
        }
        catch (OidcError e) when (e.Error == "invalid_grant")
        {
            await audit.WriteAsync(AuditEvents.OidcTokenFailed, clientId: client.Id,
                ip: ip, userAgent: userAgent, metadata: new { error = e.Error, e.Description }, ct: ct);
            throw;
        }
    }

    private async Task<TokenResponse> GrantCodeAsync(
        Client client, TokenRequest req, IPAddress? ip, string? userAgent, CancellationToken ct)
    {
        var payload = req.Code is null
            ? null
            : await codes.ConsumeAsync(req.Code, ct);
        if (payload is null)
            throw OidcError.InvalidGrant("authorization code is invalid or expired");
        if (payload.ClientId != client.Id)
            throw OidcError.InvalidGrant("code was not issued to this client");
        if (req.RedirectUri != payload.RedirectUri)
            throw OidcError.InvalidGrant("redirect_uri mismatch");
        if (!VerifyPkce(req.CodeVerifier, payload.CodeChallenge))
            throw OidcError.InvalidGrant("PKCE verification failed");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == payload.UserId, ct);
        if (user is null || user.IsDisabled)
            throw OidcError.InvalidGrant("subject unavailable");

        return await IssueTokensAsync(user, client, payload.Scope, payload.Nonce,
            "authorization_code", ip, userAgent, ct);
    }

    private async Task<TokenResponse> GrantRefreshAsync(
        Client client, TokenRequest req, IPAddress? ip, string? userAgent, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(req.RefreshToken))
            throw OidcError.InvalidRequest("refresh_token is required");

        var rotated = await refreshTokens.RotateAsync(req.RefreshToken, options.RefreshTokenTtl, ct);
        if (rotated.Status == RotateStatus.ReuseDetected)
        {
            await audit.WriteAsync(AuditEvents.OidcRefreshReuse, rotated.Payload?.UserId, client.Id,
                ip: ip, userAgent: userAgent, ct: ct);
            throw OidcError.InvalidGrant("refresh token reuse detected — grant revoked");
        }
        if (rotated.Status != RotateStatus.Rotated || rotated.Payload is null)
            throw OidcError.InvalidGrant("invalid refresh token");

        var payload = rotated.Payload;
        if (payload.ClientId != client.ClientId)
        {
            await refreshTokens.RevokeFamilyAsync(payload.FamilyId, ct);
            throw OidcError.InvalidGrant("refresh token bound to a different client");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == payload.UserId, ct);
        if (user is null || user.IsDisabled)
        {
            await refreshTokens.RevokeFamilyAsync(payload.FamilyId, ct);
            throw OidcError.InvalidGrant("subject unavailable");
        }

        var access = tokens.CreateOidcAccessToken(user, client.ClientId, payload.Scope);
        var id = tokens.CreateIdToken(user, client.ClientId, null);
        await audit.WriteAsync(AuditEvents.OidcToken, user.Id, client.Id,
            ip: ip, userAgent: userAgent, metadata: new { grant = "refresh_token" }, ct: ct);
        return new TokenResponse(access.Token, "Bearer", access.ExpiresInSeconds,
            rotated.NewToken, id.Token, payload.Scope);
    }

    /// <summary>POST /oauth/revoke — RFC 7009: client auth + best-effort revoke, always 200.</summary>
    public async Task RevokeAsync(TokenRequest req, IPAddress? ip, string? userAgent, CancellationToken ct = default)
    {
        var client = await AuthenticateClientAsync(req, ct);
        if (req.RefreshToken is { } rt)
            await refreshTokens.RevokeAsync(rt, ct);
        await audit.WriteAsync(AuditEvents.OidcRevoke, clientId: client.Id,
            ip: ip, userAgent: userAgent, ct: ct);
    }

    /// <summary>GET /oauth/userinfo claims, filtered by the token's scope (FR-B04).</summary>
    public static IReadOnlyDictionary<string, object?> UserinfoResponse(IEnumerable<Claim> claims)
    {
        var list = claims.ToList();
        var scope = list.FirstOrDefault(c => c.Type == "scope")?.Value ?? "";
        var result = new Dictionary<string, object?>
        {
            ["sub"] = list.FirstOrDefault(c => c.Type == "sub")?.Value,
        };
        if (scope.Split(' ').Contains("profile")
            && list.FirstOrDefault(c => c.Type == "name")?.Value is { } name)
            result["name"] = name;
        if (scope.Split(' ').Contains("email"))
        {
            result["email"] = list.FirstOrDefault(c => c.Type == "email")?.Value;
            if (list.FirstOrDefault(c => c.Type == "email_verified")?.Value is { } v)
                result["email_verified"] = v == "true";
        }
        return result;
    }

    // --- internals ---

    private async Task<Client> AuthenticateClientAsync(TokenRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ClientId))
            throw OidcError.InvalidClient("client authentication required");
        var client = await db.Clients.FirstOrDefaultAsync(
            c => c.ClientId == req.ClientId.Trim(), ct);
        if (client is null || !client.Active)
            throw OidcError.InvalidClient("unknown or inactive client");

        if (client.Type == ClientType.Confidential)
        {
            var ok = req.ClientSecret is { } secret
                && client.SecretHash is { } hash
                && CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(Encoding.UTF8.GetBytes(secret)),
                    Convert.FromHexString(hash));
            if (!ok)
                throw OidcError.InvalidClient("invalid client credentials");
        }
        return client;
    }

    private async Task<TokenResponse> IssueTokensAsync(
        User user, Client client, string scope, string? nonce,
        string grant, IPAddress? ip, string? userAgent, CancellationToken ct)
    {
        var access = tokens.CreateOidcAccessToken(user, client.ClientId, scope);
        var id = tokens.CreateIdToken(user, client.ClientId, nonce);

        string? refresh = null;
        if (scope.Split(' ').Contains("offline_access"))
            (refresh, _) = await refreshTokens.IssueAsync(
                new OidcRefreshPayload(user.Id, client.ClientId, scope, Guid.NewGuid()),
                options.RefreshTokenTtl, ct);

        await audit.WriteAsync(AuditEvents.OidcToken, user.Id, client.Id,
            ip: ip, userAgent: userAgent, metadata: new { grant, scope }, ct: ct);
        return new TokenResponse(access.Token, "Bearer", access.ExpiresInSeconds,
            refresh, id.Token, scope);
    }

    private static bool VerifyPkce(string? verifier, string challenge)
    {
        if (string.IsNullOrEmpty(verifier) || verifier.Length is < 43 or > 128)
            return false;
        var computed = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(challenge));
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string AppendQuery(string uri, params (string Key, string? Value)[] parameters)
    {
        var separator = uri.Contains('?') ? "&" : "?";
        return uri + separator + string.Join('&',
            parameters.Where(p => p.Value is not null)
                .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}"));
    }
}
