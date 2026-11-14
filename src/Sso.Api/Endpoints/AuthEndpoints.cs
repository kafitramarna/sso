using Microsoft.AspNetCore.Mvc;
using Sso.Api.Http;
using Sso.Application.Auth;
using Sso.Application.Errors;

namespace Sso.Api.Endpoints;

/// <summary>/v1/auth — SSO session layer, cookie `sso_rt` (KD-03).</summary>
public static class AuthEndpoints
{
    private const string RefreshCookie = "sso_rt";

    /// <summary>
    /// `sso_sess` carries the session family id with Path=/ and SameSite=Lax so
    /// /oauth/authorize can detect a live SSO session during cross-site
    /// top-level navigations — `sso_rt` stays Strict + /v1/auth-scoped (KD-03).
    /// </summary>
    private const string SessionCookie = "sso_sess";

    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/v1/auth");

        group.MapGet("/config", (AuthOptions options) =>
            ApiEnvelope.Ok(new { options.AllowSelfRegistration }))
            .AllowAnonymous();

        group.MapPost("/register", async (
                RegisterRequest request, HttpContext http, AuthService auth, CancellationToken ct) =>
            {
                await auth.RegisterAsync(request, http.Connection.RemoteIpAddress, Ua(http), ct);
                return ApiEnvelope.Ok(new
                {
                    message = "If this email can be registered, a verification link has been sent.",
                });
            })
            .AllowAnonymous()
            .RequireRateLimiting("auth");

        group.MapPost("/login", async (
                LoginRequest request, HttpContext http, AuthService auth, CancellationToken ct) =>
            {
                var result = await auth.LoginAsync(request, http.Connection.RemoteIpAddress, Ua(http), ct);
                SetSessionCookies(http, result);
                return ApiEnvelope.Ok(Body(result));
            })
            .AllowAnonymous()
            .RequireRateLimiting("auth");

        group.MapPost("/refresh", async (
                HttpContext http, AuthService auth, CancellationToken ct) =>
            {
                var cookie = http.Request.Cookies[RefreshCookie]
                    ?? throw ApiError.Unauthenticated("No refresh token.");
                var result = await auth.RefreshAsync(cookie, http.Connection.RemoteIpAddress, Ua(http), ct);
                SetSessionCookies(http, result);
                return ApiEnvelope.Ok(Body(result));
            })
            .AllowAnonymous()
            .RequireRateLimiting("auth");

        group.MapPost("/logout", async (
                HttpContext http, AuthService auth, CancellationToken ct) =>
            {
                if (http.Request.Cookies[RefreshCookie] is { } cookie)
                    await auth.LogoutAsync(cookie, http.Connection.RemoteIpAddress, Ua(http), ct);
                DeleteSessionCookies(http);
                return Results.NoContent();
            })
            .AllowAnonymous()
            .RequireRateLimiting("auth");

        group.MapPost("/verify-email", async (
                VerifyEmailRequest request, HttpContext http, AuthService auth, CancellationToken ct) =>
            {
                var result = await auth.VerifyEmailAsync(request, http.Connection.RemoteIpAddress, Ua(http), ct);
                SetSessionCookies(http, result);
                return ApiEnvelope.Ok(Body(result));
            })
            .AllowAnonymous()
            .RequireRateLimiting("auth");

        group.MapPost("/forgot-password", async (
                ForgotPasswordRequest request, HttpContext http, AuthService auth, CancellationToken ct) =>
            {
                await auth.ForgotPasswordAsync(request, http.Connection.RemoteIpAddress, Ua(http), ct);
                return ApiEnvelope.Ok(new
                {
                    message = "If this account exists, a reset link has been sent.",
                });
            })
            .AllowAnonymous()
            .RequireRateLimiting("auth");

        group.MapPost("/reset-password", async (
                ResetPasswordRequest request, HttpContext http, AuthService auth, CancellationToken ct) =>
            {
                await auth.ResetPasswordAsync(request, http.Connection.RemoteIpAddress, Ua(http), ct);
                DeleteSessionCookies(http); // all sessions were revoked anyway
                return ApiEnvelope.Ok(new { message = "Password updated." });
            })
            .AllowAnonymous()
            .RequireRateLimiting("auth");

        group.MapPost("/accept-invite", async (
                AcceptInviteRequest request, HttpContext http, AuthService auth, CancellationToken ct) =>
            {
                var result = await auth.AcceptInviteAsync(request, http.Connection.RemoteIpAddress, Ua(http), ct);
                SetSessionCookies(http, result);
                return ApiEnvelope.Ok(Body(result));
            })
            .AllowAnonymous()
            .RequireRateLimiting("auth");

        return group;
    }

    private static object Body(AuthResult result)
        => new
        {
            token = result.AccessToken,
            expires_in = result.ExpiresIn,
            user = result.User,
        };

    private static string? Ua(HttpContext http)
        => http.Request.Headers.UserAgent.FirstOrDefault();

    private static void SetSessionCookies(HttpContext http, AuthResult result)
    {
        var secure = http.RequestServices.GetRequiredService<IConfiguration>()
            .GetValue("Auth:CookieSecure", true);
        var expires = DateTimeOffset.UtcNow.AddDays(7);
        http.Response.Cookies.Append(RefreshCookie, result.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Path = "/v1/auth",
            Expires = expires,
        });
        http.Response.Cookies.Append(SessionCookie, result.FamilyId.ToString(), new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = expires,
        });
    }

    private static void DeleteSessionCookies(HttpContext http)
    {
        http.Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = "/v1/auth" });
        http.Response.Cookies.Delete(SessionCookie, new CookieOptions { Path = "/" });
    }
}
