using System.Text;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Sso.Application.Abstractions;
using Sso.Application.Oidc;
using static Sso.Application.Oidc.AuthorizeOutcome;

namespace Sso.Api.Endpoints;

/// <summary>
/// OIDC provider surface (KD-01, FR-B): authorize/token/userinfo/revoke.
/// OAuth-specified responses keep their raw shape — no ApiEnvelope.
/// </summary>
public static class OidcEndpoints
{
    private const string SessionCookie = "sso_sess";

    public static void MapOidcEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/oauth");

        // FR-B02 — browser GET; session detected via the `sso_sess` cookie.
        group.MapGet("/authorize", async (
                HttpContext http, OidcService oidc, IRefreshTokenStore sessions, CancellationToken ct) =>
            {
                var q = http.Request.Query;
                var req = new AuthorizeRequest(
                    ResponseType: q["response_type"],
                    ClientId: q["client_id"],
                    RedirectUri: q["redirect_uri"],
                    Scope: q["scope"],
                    State: q["state"],
                    Nonce: q["nonce"],
                    CodeChallenge: q["code_challenge"],
                    CodeChallengeMethod: q["code_challenge_method"]);

                Guid? userId = null;
                if (http.Request.Cookies[SessionCookie] is { } raw
                    && Guid.TryParse(raw, out var familyId))
                {
                    userId = (await sessions.ResolveFamilyAsync(familyId, ct))?.UserId;
                }

                var outcome = await oidc.DecideAuthorizeAsync(
                    req, userId, http.Request.GetEncodedUrl(), ct);

                return outcome switch
                {
                    RedirectLogin r => Results.Redirect(r.Url),
                    RedirectError r => Results.Redirect(r.Uri),
                    RedirectCode r => Results.Redirect(r.Uri),
                    InvalidRequest r => Results.Json(
                        new { error = r.Error, error_description = r.Description },
                        statusCode: StatusCodes.Status400BadRequest),
                    _ => Results.StatusCode(500),
                };
            })
            .AllowAnonymous()
            .RequireRateLimiting("auth");

        // FR-B03 — code exchange + refresh grant; client auth via Basic or body.
        group.MapPost("/token", async (
                HttpContext http, OidcService oidc, CancellationToken ct) =>
            {
                var form = await http.Request.ReadFormAsync(ct);
                var (clientId, clientSecret) = ClientCredentials(http, form);
                var req = new TokenRequest(
                    GrantType: Empty(form["grant_type"]),
                    Code: Empty(form["code"]),
                    RedirectUri: Empty(form["redirect_uri"]),
                    ClientId: clientId,
                    ClientSecret: clientSecret,
                    CodeVerifier: Empty(form["code_verifier"]),
                    RefreshToken: Empty(form["refresh_token"]));

                try
                {
                    var res = await oidc.ExchangeTokenAsync(
                        req, http.Connection.RemoteIpAddress, Ua(http), ct);

                    var body = new Dictionary<string, object?>
                    {
                        ["access_token"] = res.AccessToken,
                        ["token_type"] = res.TokenType,
                        ["expires_in"] = res.ExpiresIn,
                        ["scope"] = res.Scope,
                    };
                    if (res.RefreshToken is not null) body["refresh_token"] = res.RefreshToken;
                    if (res.IdToken is not null) body["id_token"] = res.IdToken;
                    return Results.Json(body);
                }
                catch (OidcError e)
                {
                    return OidcErrorResult(e);
                }
            })
            .AllowAnonymous()
            .RequireRateLimiting("auth");

        // FR-B04 — Bearer = OIDC access token (aud = client_id, validated by the
        // "oidc" scheme registered in Program).
        group.MapGet("/userinfo", (HttpContext http) =>
            Results.Json(OidcService.UserinfoResponse(http.User.Claims)))
            .RequireAuthorization("oidc");

        // RFC 7009 — client-authenticated revocation, always 200 on valid client.
        group.MapPost("/revoke", async (
                HttpContext http, OidcService oidc, CancellationToken ct) =>
            {
                var form = await http.Request.ReadFormAsync(ct);
                var (clientId, clientSecret) = ClientCredentials(http, form);
                var req = new TokenRequest(null, null, null, clientId, clientSecret,
                    null, Empty(form["token"]));
                try
                {
                    await oidc.RevokeAsync(req, http.Connection.RemoteIpAddress, Ua(http), ct);
                    return Results.Ok();
                }
                catch (OidcError e)
                {
                    return OidcErrorResult(e);
                }
            })
            .AllowAnonymous()
            .RequireRateLimiting("auth");
    }

    /// <summary>Basic header (RFC 6749 §2.3.1) wins over client_secret_post.</summary>
    private static (string? Id, string? Secret) ClientCredentials(HttpContext http, IFormCollection form)
    {
        string? id = Empty(form["client_id"]);
        string? secret = Empty(form["client_secret"]);

        var auth = http.Request.Headers.Authorization.ToString();
        if (auth.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(auth[6..].Trim()));
                var i = decoded.IndexOf(':');
                if (i > 0)
                {
                    id = Uri.UnescapeDataString(decoded[..i]);
                    secret = Uri.UnescapeDataString(decoded[(i + 1)..]);
                }
            }
            catch (FormatException) { /* malformed Basic — service returns invalid_client */ }
        }
        return (id, secret);
    }

    /// <summary>invalid_client → 401 per RFC; everything else → 400.</summary>
    private static IResult OidcErrorResult(OidcError e) => Results.Json(
        new { error = e.Error, error_description = e.Description },
        statusCode: e.Error == "invalid_client"
            ? StatusCodes.Status401Unauthorized
            : StatusCodes.Status400BadRequest);

    private static string? Empty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? Ua(HttpContext http)
        => http.Request.Headers.UserAgent.FirstOrDefault();
}
