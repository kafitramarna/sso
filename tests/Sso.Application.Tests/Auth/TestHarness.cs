using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sso.Application.Abstractions;
using Sso.Application.Auth;
using Sso.Domain.Entities;
using Sso.Domain.Enums;
using Sso.Infrastructure.Persistence;

namespace Sso.Application.Tests.Auth;

public sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

public sealed class FakeTokenService : ITokenService
{
    public AccessTokenResult CreateSessionToken(User user, Guid? sessionFamily = null)
        => new($"access-{user.Id}", 900, DateTimeOffset.UtcNow.AddMinutes(15));

    public AccessTokenResult CreateOidcAccessToken(User user, string clientId, string scope)
        => new($"oidc-access-{clientId}-{user.Id}", 900, DateTimeOffset.UtcNow.AddMinutes(15));

    public AccessTokenResult CreateIdToken(User user, string clientId, string? nonce)
        => new($"id-token-{clientId}-{user.Id}", 900, DateTimeOffset.UtcNow.AddMinutes(15));
}

/// <summary>In-memory mirror of the Redis family-rotation contract (KD-03).</summary>
public sealed class FakeRefreshTokenStore : IRefreshTokenStore
{
    private sealed record Entry(Guid UserId, Guid FamilyId, SessionMeta Meta, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt);

    private readonly Dictionary<string, Entry> _tokens = new();
    private readonly Dictionary<string, Entry> _tombstones = new(); // rotated token → last payload (reuse detection)

    public int IssueCount { get; private set; }
    public List<Guid> RevokedFamilies { get; } = new();

    public Task<(string Token, Guid FamilyId)> IssueAsync(
        Guid userId, Guid? familyId, SessionMeta meta, TimeSpan ttl, CancellationToken ct = default)
    {
        IssueCount++;
        var token = $"rt-{Guid.NewGuid():N}";
        var family = familyId ?? Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        _tokens[token] = new Entry(userId, family, meta, now, now);
        return Task.FromResult((token, family));
    }

    public Task<RotateResult> RotateAsync(string token, TimeSpan ttl, CancellationToken ct = default)
    {
        if (!_tokens.Remove(token, out var entry))
        {
            if (_tombstones.TryGetValue(token, out var stale))
            {
                RevokedFamilies.Add(stale.FamilyId);
                foreach (var k in _tokens.Where(t => t.Value.FamilyId == stale.FamilyId).Select(t => t.Key).ToList())
                    _tokens.Remove(k);
                return Task.FromResult(RotateResult.Reuse(
                    new RefreshPayload(stale.UserId, stale.FamilyId, stale.Meta)));
            }
            return Task.FromResult(RotateResult.Invalid);
        }

        _tombstones[token] = entry;
        var newToken = $"rt-{Guid.NewGuid():N}";
        _tokens[newToken] = entry with { LastSeenAt = DateTimeOffset.UtcNow };
        return Task.FromResult(RotateResult.Rotated(
            new RefreshPayload(entry.UserId, entry.FamilyId, entry.Meta), newToken));
    }

    public Task<RefreshPayload?> ResolveAsync(string token, CancellationToken ct = default)
        => Task.FromResult(_tokens.TryGetValue(token, out var e)
            ? new RefreshPayload(e.UserId, e.FamilyId, e.Meta)
            : null);

    public Task<RefreshPayload?> ResolveFamilyAsync(Guid familyId, CancellationToken ct = default)
        => Task.FromResult(_tokens.Values
            .Where(e => e.FamilyId == familyId)
            .OrderByDescending(e => e.LastSeenAt)
            .Select(e => new RefreshPayload(e.UserId, e.FamilyId, e.Meta))
            .FirstOrDefault());

    public Task RevokeFamilyAsync(Guid familyId, CancellationToken ct = default)
    {
        RevokedFamilies.Add(familyId);
        foreach (var k in _tokens.Where(t => t.Value.FamilyId == familyId).Select(t => t.Key).ToList())
            _tokens.Remove(k);
        return Task.CompletedTask;
    }

    public Task RevokeAllAsync(Guid userId, Guid? exceptFamilyId = null, CancellationToken ct = default)
    {
        foreach (var k in _tokens.Where(t => t.Value.UserId == userId && t.Value.FamilyId != exceptFamilyId).Select(t => t.Key).ToList())
            _tokens.Remove(k);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SessionInfo>> ListSessionsAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SessionInfo>>(_tokens.Values
            .Where(e => e.UserId == userId)
            .GroupBy(e => e.FamilyId)
            .Select(g => new SessionInfo(g.Key, g.First().Meta.UserAgent, g.First().Meta.Ip, g.Min(e => e.CreatedAt), g.Max(e => e.LastSeenAt)))
            .ToList());

    public bool HasValidToken(string token) => _tokens.ContainsKey(token);
}

public sealed record SentNotification(string Kind, string Email, string Url);

public sealed class FakeNotifier : INotifier
{
    public List<SentNotification> Sent { get; } = new();

    public Task NotifyVerifyEmailAsync(string email, string verifyUrl, CancellationToken ct = default)
        { Sent.Add(new("verify", email, verifyUrl)); return Task.CompletedTask; }

    public Task NotifyPasswordResetAsync(string email, string resetUrl, CancellationToken ct = default)
        { Sent.Add(new("reset", email, resetUrl)); return Task.CompletedTask; }

    public Task NotifyInvitedAsync(string email, string inviteUrl, CancellationToken ct = default)
        { Sent.Add(new("invite", email, inviteUrl)); return Task.CompletedTask; }
}

public sealed class FakeAuditWriter : IAuditWriter
{
    public List<(string Event, Guid? UserId, IPAddress? Ip)> Entries { get; } = new();

    public Task WriteAsync(string eventName, Guid? userId = null, Guid? clientId = null,
        IPAddress? ip = null, string? userAgent = null, object? metadata = null, CancellationToken ct = default)
        { Entries.Add((eventName, userId, ip)); return Task.CompletedTask; }
}

/// <summary>Builds an AuthService against EF InMemory + fakes.</summary>
public sealed class TestHarness : IDisposable
{
    public SsoDbContext Db { get; }
    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
    public FakeRefreshTokenStore Refresh { get; } = new();
    public FakeNotifier Notifier { get; } = new();
    public FakeAuditWriter Audit { get; } = new();
    public PasswordHasher<User> Hasher { get; } = new();
    public AuthService Service { get; }

    public TestHarness(bool allowSelfRegistration = true)
    {
        var options = new DbContextOptionsBuilder<SsoDbContext>()
            .UseInMemoryDatabase($"sso-test-{Guid.NewGuid():N}")
            .Options;
        Db = new SsoDbContext(options);

        Service = new AuthService(
            Db, Hasher, new FakeTokenService(), Refresh, Notifier, Audit, Clock,
            new AuthOptions { AllowSelfRegistration = allowSelfRegistration, WebBaseUrl = "http://localhost:5175" });
    }

    public User SeedUser(string email = "user@example.com", string password = "correct-password-1",
        bool verified = true, bool admin = false, bool disabled = false)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            EmailVerifiedAt = verified ? Clock.UtcNow : null,
            IsSsoAdmin = admin,
            Status = disabled ? UserStatus.Disabled : UserStatus.Active,
            CreatedAt = Clock.UtcNow,
            UpdatedAt = Clock.UtcNow,
        };
        user.PasswordHash = Hasher.HashPassword(user, password);
        Db.Users.Add(user);
        Db.SaveChanges();
        return user;
    }

    /// <summary>Extracts the raw token out of the link that was "emailed".</summary>
    public string TokenFromLastUrl()
        => Notifier.Sent[^1].Url.Split("token=")[1];

    public void Dispose() => Db.Dispose();
}
