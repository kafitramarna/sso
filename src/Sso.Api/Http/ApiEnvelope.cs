using Sso.Application.Errors;

namespace Sso.Api.Http;

/// <summary>Response envelope per docs/01-PRD §10.</summary>
public static class ApiEnvelope
{
    public static IResult Ok(object? data, int statusCode = StatusCodes.Status200OK, object? meta = null)
        => Results.Json(new
        {
            success = true,
            data,
            meta = Meta(meta),
        }, statusCode: statusCode);

    public static IResult Error(HttpContext http, int statusCode, string code, string message, object? details = null)
        => Results.Json(new
        {
            success = false,
            error = new { code, message, details },
            meta = Meta(),
        }, statusCode: statusCode);

    public static object Meta(object? extra = null)
        => extra is null
            ? new { request_id = System.Diagnostics.Activity.Current?.Id ?? Guid.NewGuid().ToString(), timestamp = DateTimeOffset.UtcNow }
            : new { request_id = System.Diagnostics.Activity.Current?.Id ?? Guid.NewGuid().ToString(), timestamp = DateTimeOffset.UtcNow, extra };
}

/// <summary>Maps ApiError and unhandled exceptions to the error envelope.</summary>
public sealed class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);

            // Envelope bare 401/403 produced by the auth middleware.
            if (!context.Response.HasStarted && context.Response.StatusCode is 401 or 403)
            {
                var code = context.Response.StatusCode == 401 ? "unauthenticated" : "forbidden";
                var message = context.Response.StatusCode == 401
                    ? "Authentication required."
                    : "Insufficient permissions.";
                await context.Response.WriteAsJsonAsync(new
                {
                    success = false,
                    error = new { code, message, details = (object?)null },
                    meta = ApiEnvelope.Meta(),
                });
            }
        }
        catch (ApiError error)
        {
            context.Response.StatusCode = error.StatusCode;
            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                error = new { code = error.Code, message = error.Message, details = error.Details },
                meta = ApiEnvelope.Meta(),
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception on {Path}", context.Request.Path);
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                error = new { code = "internal_error", message = "An unexpected error occurred.", details = (object?)null },
                meta = ApiEnvelope.Meta(),
            });
        }
    }
}
