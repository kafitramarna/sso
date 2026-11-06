using Sso.Api.Auth;
using Sso.Api.Http;
using Sso.Application.Auth;
using Sso.Application.Errors;

namespace Sso.Api.Endpoints;

/// <summary>
/// /admin/* — KD-06: sso_admin JWT or X-Admin-Key. The admin UI lives in
/// Sso.Web (/admin area); these endpoints serve it and ops scripts.
/// </summary>
public static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/admin");

        group.MapPost("/invites", async (
                CreateInviteRequest request, HttpContext http, AuthService auth,
                IConfiguration config, CancellationToken ct) =>
            {
                RequireAdmin(http, config);
                var invite = await auth.CreateInviteAsync(
                    request, http.User.UserId() ?? Guid.Empty,
                    http.Connection.RemoteIpAddress, Ua(http), ct);
                return ApiEnvelope.Ok(new
                {
                    email = invite.Email,
                    invite_url = invite.InviteUrl,
                    expires_at = invite.ExpiresAt,
                }, StatusCodes.Status201Created);
            });

        group.MapPost("/users/{userId:guid}/disable", async (
                Guid userId, HttpContext http, AuthService auth,
                IConfiguration config, CancellationToken ct) =>
            {
                RequireAdmin(http, config);
                await auth.SetUserDisabledAsync(
                    userId, true, http.User.UserId() ?? Guid.Empty,
                    http.Connection.RemoteIpAddress, Ua(http), ct);
                return Results.NoContent();
            });

        group.MapPost("/users/{userId:guid}/enable", async (
                Guid userId, HttpContext http, AuthService auth,
                IConfiguration config, CancellationToken ct) =>
            {
                RequireAdmin(http, config);
                await auth.SetUserDisabledAsync(
                    userId, false, http.User.UserId() ?? Guid.Empty,
                    http.Connection.RemoteIpAddress, Ua(http), ct);
                return Results.NoContent();
            });

        return group;
    }

    private static void RequireAdmin(HttpContext http, IConfiguration config)
    {
        if (!http.IsAdminRequest(config))
            throw ApiError.Forbidden("Admin access required.");
    }

    private static string? Ua(HttpContext http)
        => http.Request.Headers.UserAgent.FirstOrDefault();
}
