using Sso.Api.Auth;
using Sso.Api.Http;
using Sso.Application.Auth;
using Sso.Application.Errors;
using Sso.Application.Oidc;

namespace Sso.Api.Endpoints;

/// <summary>
/// /admin/* — KD-06: sso_admin JWT or X-Admin-Key. The admin UI lives in
/// Sso.Web (/admin area); these endpoints serve it and ops scripts.
/// </summary>
public static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/v1/admin");

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

        group.MapGet("/users", async (
                string? search, int? page, HttpContext http, AuthService auth,
                IConfiguration config, CancellationToken ct) =>
            {
                RequireAdmin(http, config);
                var (users, total) = await auth.ListUsersAsync(search, page ?? 1, 25, ct);
                return ApiEnvelope.Ok(new { users, total });
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

        // --- client registry (FR-C) — plaintext secret appears exactly once ---

        group.MapGet("/clients", async (
                HttpContext http, ClientsService clients, IConfiguration config,
                CancellationToken ct) =>
            {
                RequireAdmin(http, config);
                return ApiEnvelope.Ok(new { clients = await clients.ListAsync(ct) });
            });

        group.MapPost("/clients", async (
                CreateClientRequest request, HttpContext http, ClientsService clients,
                IConfiguration config, CancellationToken ct) =>
            {
                RequireAdmin(http, config);
                var result = await clients.CreateAsync(
                    request, http.User.UserId() ?? Guid.Empty,
                    http.Connection.RemoteIpAddress, Ua(http), ct);
                return ApiEnvelope.Ok(new
                {
                    client = result.Client,
                    client_secret = result.PlainSecret, // shown once — never again (FR-C02)
                }, StatusCodes.Status201Created);
            });

        group.MapPatch("/clients/{clientId:guid}", async (
                Guid clientId, UpdateClientRequest request, HttpContext http,
                ClientsService clients, IConfiguration config, CancellationToken ct) =>
            {
                RequireAdmin(http, config);
                var dto = await clients.UpdateAsync(
                    clientId, request, http.User.UserId() ?? Guid.Empty,
                    http.Connection.RemoteIpAddress, Ua(http), ct);
                return ApiEnvelope.Ok(new { client = dto });
            });

        group.MapPost("/clients/{clientId:guid}/rotate-secret", async (
                Guid clientId, HttpContext http, ClientsService clients,
                IConfiguration config, CancellationToken ct) =>
            {
                RequireAdmin(http, config);
                var result = await clients.RotateSecretAsync(
                    clientId, http.User.UserId() ?? Guid.Empty,
                    http.Connection.RemoteIpAddress, Ua(http), ct);
                return ApiEnvelope.Ok(new
                {
                    client = result.Client,
                    client_secret = result.PlainSecret,
                });
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
