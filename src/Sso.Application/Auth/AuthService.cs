using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sso.Application.Abstractions;
using Sso.Application.Errors;
using Sso.Domain.Entities;
using Sso.Domain.Enums;
using Sso.Domain.Rules;

namespace Sso.Application.Auth;

/// <summary>
/// SSO session layer (/v1/auth, KD-03) — distinct from the OIDC provider layer
/// (/oauth/*, Fase 2). Never leak whether an email exists: outward responses are
/// identical for unknown accounts (PRD §15).
/// </summary>
public sealed partial class AuthService(
    ISsoDbContext db,
    IPasswordHasher<User> passwordHasher,
    ITokenService tokens,
    IRefreshTokenStore refreshTokens,
    INotifier notifier,
    IAuditWriter audit,
    IClock clock,
    AuthOptions options)
{
    // --- register (invite-only in production, KD-09) ---

    public async Task RegisterAsync(RegisterRequest request, IPAddress? ip, string? userAgent, CancellationToken ct = default)
    {
        if (!options.AllowSelfRegistration)
            throw ApiError.Forbidden("Registration is invite-only.");

        var email = request.Email.Trim().ToLowerInvariant();
        ValidateCredentials(request.Password, email);

        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            return; // same outward result as a fresh register — no email enumeration

        var now = clock.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = Clean(request.DisplayName),
            CreatedAt = now,
            UpdatedAt = now,
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        var token = await IssueOneTimeTokenAsync(user.Id, OneTimeTokenKind.VerifyEmail, options.VerifyTokenTtl, ct);
        await notifier.NotifyVerifyEmailAsync(user.Email, Url("verify-email", token), ct);
        await audit.WriteAsync(AuditEvents.AuthRegistered, user.Id, ip: ip, userAgent: userAgent, ct: ct);
    }

    // --- login / refresh / logout ---

    public async Task<AuthResult> LoginAsync(LoginRequest request, IPAddress? ip, string? userAgent, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        var now = clock.UtcNow;

        if (user is null || user.PasswordHash is null
            || passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password)
                == PasswordVerificationResult.Failed)
        {
            if (user is not null)
            {
                user.FailedLoginCount++;
                if (LockoutPolicy.LockoutFor(user.FailedLoginCount) is { } duration)
                    user.LockedUntil = now + duration;
                user.UpdatedAt = now;
                await db.SaveChangesAsync(ct);
            }
            await audit.WriteAsync(AuditEvents.AuthLoginFailed, user?.Id, ip: ip, userAgent: userAgent, ct: ct);
            throw ApiError.Unauthenticated("Invalid email or password.");
        }

        // Credentials are valid from here — distinct codes no longer enumerate accounts.
        if (user.LockedUntil is { } locked && locked > now)
            throw ApiError.Unauthenticated("Account temporarily locked. Try again later.");
        if (user.IsDisabled)
            throw new ApiError(403, "account_disabled", "This account has been disabled.");
        if (!user.EmailVerified)
            throw new ApiError(403, "email_not_verified", "Verify your email address before signing in.");

        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.LastLoginAt = now;
        user.LastLoginIp = ip?.ToString();
        user.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(AuditEvents.AuthLogin, user.Id, ip: ip, userAgent: userAgent, ct: ct);
        return await IssueSessionAsync(user, null, ip, userAgent, ct);
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken, IPAddress? ip, string? userAgent, CancellationToken ct = default)
    {
        var rotated = await refreshTokens.RotateAsync(refreshToken, options.RefreshTokenTtl, ct);

        if (rotated.Status == RotateStatus.ReuseDetected)
        {
            await audit.WriteAsync(AuditEvents.AuthRefreshReuse, rotated.Payload?.UserId, ip: ip, userAgent: userAgent, ct: ct);
            throw ApiError.Unauthenticated("Session expired. Please sign in again.");
        }
        if (rotated.Status != RotateStatus.Rotated || rotated.Payload is null)
            throw ApiError.Unauthenticated("Session expired. Please sign in again.");

        var payload = rotated.Payload;
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == payload.UserId, ct);
        if (user is null || user.IsDisabled)
        {
            await refreshTokens.RevokeFamilyAsync(payload.FamilyId, ct);
            throw ApiError.Unauthenticated("Session expired. Please sign in again.");
        }

        await audit.WriteAsync(AuditEvents.AuthRefresh, user.Id, ip: ip, userAgent: userAgent, ct: ct);
        var access = tokens.CreateSessionToken(user, payload.FamilyId);
        return new AuthResult(access.Token, access.ExpiresInSeconds, rotated.NewToken!, payload.FamilyId, user.ToDto());
    }

    public async Task LogoutAsync(string refreshToken, IPAddress? ip, string? userAgent, CancellationToken ct = default)
    {
        var payload = await refreshTokens.ResolveAsync(refreshToken, ct);
        if (payload is not null)
        {
            await refreshTokens.RevokeFamilyAsync(payload.FamilyId, ct);
            await audit.WriteAsync(AuditEvents.AuthLogout, payload.UserId, ip: ip, userAgent: userAgent, ct: ct);
        }
    }

    // --- email verification (FR-A01) ---

    public async Task<AuthResult> VerifyEmailAsync(VerifyEmailRequest request, IPAddress? ip, string? userAgent, CancellationToken ct = default)
    {
        var token = await ConsumeAsync(request.Token, OneTimeTokenKind.VerifyEmail, ct);
        var user = await db.Users.FirstAsync(u => u.Id == token.UserId, ct);

        user.EmailVerifiedAt ??= clock.UtcNow;
        user.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(AuditEvents.EmailVerified, user.Id, ip: ip, userAgent: userAgent, ct: ct);
        return await IssueSessionAsync(user, null, ip, userAgent, ct);
    }

    // --- password reset (FR-A03) ---

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, IPAddress? ip, string? userAgent, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user is { PasswordHash: not null })
        {
            var token = await IssueOneTimeTokenAsync(user.Id, OneTimeTokenKind.ResetPassword, options.ResetTokenTtl, ct);
            await notifier.NotifyPasswordResetAsync(user.Email, Url("reset-password", token), ct);
        }
        await audit.WriteAsync(AuditEvents.PasswordResetRequested, user?.Id, ip: ip, userAgent: userAgent, ct: ct);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, IPAddress? ip, string? userAgent, CancellationToken ct = default)
    {
        RequireValidPassword(request.Password);
        var token = await ConsumeAsync(request.Token, OneTimeTokenKind.ResetPassword, ct);
        var user = await db.Users.FirstAsync(u => u.Id == token.UserId, ct);

        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        await refreshTokens.RevokeAllAsync(user.Id, null, ct);
        await audit.WriteAsync(AuditEvents.PasswordReset, user.Id, ip: ip, userAgent: userAgent, ct: ct);
    }

    // --- invite (FR-A04) ---

    public async Task<InviteResult> CreateInviteAsync(CreateInviteRequest request, Guid actorUserId, IPAddress? ip, string? userAgent, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (!EmailRegex().IsMatch(email))
            throw ApiError.Validation("Invalid email address.", new { errors = new[] { "email_invalid" } });
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            throw ApiError.Conflict("A user with this email already exists.");

        var now = clock.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = Clean(request.DisplayName),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        var token = await IssueOneTimeTokenAsync(user.Id, OneTimeTokenKind.Invite, options.InviteTokenTtl, ct);
        var url = Url("accept-invite", token);
        await notifier.NotifyInvitedAsync(email, url, ct);
        await audit.WriteAsync(AuditEvents.InviteCreated, user.Id, ip: ip, userAgent: userAgent,
            metadata: new { actor = actorUserId }, ct: ct);
        return new InviteResult(email, url, now + options.InviteTokenTtl);
    }

    public async Task<AuthResult> AcceptInviteAsync(AcceptInviteRequest request, IPAddress? ip, string? userAgent, CancellationToken ct = default)
    {
        RequireValidPassword(request.Password);
        var token = await ConsumeAsync(request.Token, OneTimeTokenKind.Invite, ct);
        var user = await db.Users.FirstAsync(u => u.Id == token.UserId, ct);

        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        if (request.DisplayName is not null)
            user.DisplayName = Clean(request.DisplayName);
        user.EmailVerifiedAt ??= clock.UtcNow;
        user.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(AuditEvents.InviteAccepted, user.Id, ip: ip, userAgent: userAgent, ct: ct);
        return await IssueSessionAsync(user, null, ip, userAgent, ct);
    }

    // --- me / sessions ---

    public async Task<UserDto> MeAsync(Guid userId, CancellationToken ct = default)
        => (await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw ApiError.NotFound()).ToDto();

    public async Task<UserDto> UpdateMeAsync(Guid userId, UpdateMeRequest request, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw ApiError.NotFound();
        user.DisplayName = Clean(request.DisplayName);
        user.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return user.ToDto();
    }

    public async Task ChangePasswordAsync(Guid userId, Guid currentFamilyId, ChangePasswordRequest request, IPAddress? ip, string? userAgent, CancellationToken ct = default)
    {
        RequireValidPassword(request.NewPassword);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw ApiError.Unauthenticated();
        if (user.PasswordHash is null
            || passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword)
                == PasswordVerificationResult.Failed)
            throw ApiError.Unauthenticated("Current password is incorrect.");

        user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        // FR-A05: every other session dies; the current family survives.
        await refreshTokens.RevokeAllAsync(user.Id, currentFamilyId, ct);
        await audit.WriteAsync(AuditEvents.PasswordChanged, user.Id, ip: ip, userAgent: userAgent, ct: ct);
    }

    public async Task<IReadOnlyList<SessionDto>> ListSessionsAsync(Guid userId, Guid currentFamilyId, CancellationToken ct = default)
        => (await refreshTokens.ListSessionsAsync(userId, ct))
            .Select(s => new SessionDto(s.FamilyId, s.UserAgent, s.Ip, s.CreatedAt, s.LastSeenAt,
                s.FamilyId == currentFamilyId))
            .ToList();

    public async Task RevokeSessionAsync(Guid userId, Guid familyId, IPAddress? ip, string? userAgent, CancellationToken ct = default)
    {
        // Ownership check — a user may only kill their own session families.
        var mine = await refreshTokens.ListSessionsAsync(userId, ct);
        if (mine.All(s => s.FamilyId != familyId))
            throw ApiError.NotFound("Session not found.");
        await refreshTokens.RevokeFamilyAsync(familyId, ct);
        await audit.WriteAsync(AuditEvents.SessionRevoked, userId, ip: ip, userAgent: userAgent,
            metadata: new { family_id = familyId }, ct: ct);
    }

    public async Task RevokeAllSessionsAsync(Guid userId, Guid? exceptFamilyId, IPAddress? ip, string? userAgent, CancellationToken ct = default)
    {
        await refreshTokens.RevokeAllAsync(userId, exceptFamilyId, ct);
        await audit.WriteAsync(AuditEvents.SessionsRevokedAll, userId, ip: ip, userAgent: userAgent, ct: ct);
    }

    // --- admin ---

    public async Task<(IReadOnlyList<UserDto> Users, int Total)> ListUsersAsync(
        string? search, int page, int pageSize, CancellationToken ct = default)
    {
        var q = db.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(u => u.Email.Contains(s) || (u.DisplayName != null && u.DisplayName.Contains(s)));
        }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(u => u.Email)
            .Skip(Math.Max(page - 1, 0) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (rows.ConvertAll(u => u.ToDto()), total);
    }

    public async Task SetUserDisabledAsync(Guid userId, bool disabled, Guid actorUserId, IPAddress? ip, string? userAgent, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw ApiError.NotFound();
        user.Status = disabled ? UserStatus.Disabled : UserStatus.Active;
        user.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        if (disabled)
            await refreshTokens.RevokeAllAsync(user.Id, null, ct);
        await audit.WriteAsync(disabled ? AuditEvents.UserDisabled : AuditEvents.UserEnabled,
            user.Id, ip: ip, userAgent: userAgent, metadata: new { actor = actorUserId }, ct: ct);
    }

    // --- helpers ---

    private async Task<AuthResult> IssueSessionAsync(User user, Guid? familyId, IPAddress? ip, string? userAgent, CancellationToken ct)
    {
        var (refresh, family) = await refreshTokens.IssueAsync(
            user.Id, familyId, new SessionMeta(userAgent, ip?.ToString()), options.RefreshTokenTtl, ct);
        var access = tokens.CreateSessionToken(user, family);
        return new AuthResult(access.Token, access.ExpiresInSeconds, refresh, family, user.ToDto());
    }

    private async Task<string> IssueOneTimeTokenAsync(Guid userId, OneTimeTokenKind kind, TimeSpan ttl, CancellationToken ct)
    {
        var raw = Base64Url(RandomNumberGenerator.GetBytes(32));
        var now = clock.UtcNow;
        db.OneTimeTokens.Add(new OneTimeToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Kind = kind,
            TokenHash = Hash(raw),
            ExpiresAt = now + ttl,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        return raw;
    }

    /// <summary>
    /// Single-use consume. On relational providers: atomic UPDATE ... WHERE
    /// used_at IS NULL (race-safe). Non-relational (tests): load-check-set.
    /// </summary>
    private async Task<OneTimeToken> ConsumeAsync(string raw, OneTimeTokenKind kind, CancellationToken ct)
    {
        var hash = Hash(raw);
        var now = clock.UtcNow;

        if (db.Database.IsRelational())
        {
            var rows = await db.OneTimeTokens
                .Where(t => t.TokenHash == hash && t.Kind == kind && t.UsedAt == null && t.ExpiresAt > now)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), ct);
            if (rows == 0)
                throw ApiError.Unauthenticated("This link is invalid or has expired.");
            return await db.OneTimeTokens.AsNoTracking()
                .FirstAsync(t => t.TokenHash == hash && t.Kind == kind, ct);
        }

        var token = await db.OneTimeTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.Kind == kind, ct);
        if (token is null || !token.IsUsable(now))
            throw ApiError.Unauthenticated("This link is invalid or has expired.");
        token.UsedAt = now;
        await db.SaveChangesAsync(ct);
        return token;
    }

    private string Url(string path, string token)
        => $"{options.WebBaseUrl.TrimEnd('/')}/{path}?token={Uri.EscapeDataString(token)}";

    private static void RequireValidPassword(string password)
    {
        var errors = PasswordPolicy.Validate(password);
        if (errors.Count > 0)
            throw ApiError.Validation("Password does not meet the requirements.", new { errors });
    }

    private static void ValidateCredentials(string password, string email)
    {
        var errors = new List<string>();
        if (!EmailRegex().IsMatch(email)) errors.Add("email_invalid");
        errors.AddRange(PasswordPolicy.Validate(password));
        if (errors.Count > 0)
            throw ApiError.Validation("Registration validation failed.", new { errors });
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Hash(string raw)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();
}
