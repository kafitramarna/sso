using Sso.Domain.Entities;

namespace Sso.Application.Auth;

public sealed record RegisterRequest(string Email, string Password, string? DisplayName);

public sealed record LoginRequest(string Email, string Password);

public sealed record AcceptInviteRequest(string Token, string Password, string? DisplayName);

public sealed record VerifyEmailRequest(string Token);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Token, string Password);

public sealed record UpdateMeRequest(string? DisplayName);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record CreateInviteRequest(string Email, string? DisplayName);

public sealed record UserDto(
    Guid Id,
    string Email,
    string? DisplayName,
    bool EmailVerified,
    bool IsSsoAdmin,
    string Status,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt);

public sealed record AuthResult(
    string AccessToken,
    int ExpiresIn,
    string RefreshToken,
    Guid FamilyId,
    UserDto User);

public sealed record SessionDto(
    Guid FamilyId,
    string? UserAgent,
    string? Ip,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    bool Current);

public sealed record InviteResult(string Email, string InviteUrl, DateTimeOffset ExpiresAt);

public static class AuthMapping
{
    public static UserDto ToDto(this User u)
        => new(u.Id, u.Email, u.DisplayName, u.EmailVerified, u.IsSsoAdmin,
               u.Status.ToString().ToLowerInvariant(), u.LastLoginAt, u.CreatedAt);
}
