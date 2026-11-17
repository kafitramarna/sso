using Sso.Api.Auth;
using Sso.Api.Http;
using Sso.Application.Auth;
using Sso.Application.Errors;
using Sso.Application.Oidc;

namespace Sso.Api.Endpoints;

/// <summary>/v1/me + /v1/sessions — authenticated SSO session surface (FR-A05, FR-D01).</summary>
public static class MeEndpoints
{
    public static RouteGroupBuilder MapMeEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/v1").RequireAuthorization();

        group.MapGet("/me", async (HttpContext http, AuthService auth, CancellationToken ct) =>
        {
            var user = await auth.MeAsync(UserId(http), ct);
            return ApiEnvelope.Ok(new { user });
        });

        group.MapPatch("/me", async (
                UpdateMeRequest request, HttpContext http, AuthService auth, CancellationToken ct) =>
        {
            var user = await auth.UpdateMeAsync(UserId(http), request, ct);
            return ApiEnvelope.Ok(new { user });
        });

        // KD-14 — apps the signed-in user may launch from /dashboard.
        group.MapGet("/me/apps", async (HttpContext http, AssignmentsService apps, CancellationToken ct) =>
        {
            return ApiEnvelope.Ok(new { apps = await apps.ListForUserAsync(UserId(http), ct) });
        });

        group.MapPost("/me/change-password", async (
                ChangePasswordRequest request, HttpContext http, AuthService auth, CancellationToken ct) =>
        {
            await auth.ChangePasswordAsync(
                UserId(http), http.User.SessionFamily() ?? Guid.Empty,
                request, http.Connection.RemoteIpAddress, Ua(http), ct);
            return ApiEnvelope.Ok(new { message = "Password changed. Other sessions were signed out." });
        });

        group.MapGet("/sessions", async (HttpContext http, AuthService auth, CancellationToken ct) =>
        {
            var sessions = await auth.ListSessionsAsync(
                UserId(http), http.User.SessionFamily() ?? Guid.Empty, ct);
            return ApiEnvelope.Ok(new { sessions });
        });

        group.MapDelete("/sessions/{familyId:guid}", async (
                Guid familyId, HttpContext http, AuthService auth, CancellationToken ct) =>
        {
            await auth.RevokeSessionAsync(
                UserId(http), familyId, http.Connection.RemoteIpAddress, Ua(http), ct);
            return Results.NoContent();
        });

        group.MapPost("/sessions/revoke-all", async (
                HttpContext http, AuthService auth, CancellationToken ct) =>
        {
            await auth.RevokeAllSessionsAsync(
                UserId(http), http.User.SessionFamily(),
                http.Connection.RemoteIpAddress, Ua(http), ct);
            return Results.NoContent();
        });

        return group;
    }

    private static Guid UserId(HttpContext http)
        => http.User.UserId() ?? throw ApiError.Unauthenticated();

    private static string? Ua(HttpContext http)
        => http.Request.Headers.UserAgent.FirstOrDefault();
}
