namespace Sso.Application.Errors;

/// <summary>Maps to the error envelope in docs/01-PRD §10.</summary>
public sealed class ApiError : Exception
{
    public int StatusCode { get; }
    public string Code { get; }
    public object? Details { get; }

    public ApiError(int statusCode, string code, string message, object? details = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Details = details;
    }

    public static ApiError Validation(string message, object? details = null)
        => new(400, "validation_failed", message, details);

    public static ApiError Unauthenticated(string message = "Authentication required.")
        => new(401, "unauthenticated", message);

    public static ApiError Forbidden(string message = "Insufficient permissions.")
        => new(403, "forbidden", message);

    public static ApiError NotFound(string message = "Resource not found.")
        => new(404, "not_found", message);

    public static ApiError Conflict(string message, object? details = null)
        => new(409, "conflict", message, details);

    public static ApiError Unprocessable(string message, object? details = null)
        => new(422, "unprocessable", message, details);
}
