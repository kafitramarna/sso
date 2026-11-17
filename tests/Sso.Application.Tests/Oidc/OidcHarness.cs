using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Sso.Application.Abstractions;
using Sso.Application.Oidc;
using Sso.Domain.Entities;
using Sso.Domain.Enums;
using Sso.Infrastructure.Persistence;

namespace Sso.Application.Tests.Oidc;

public sealed class FakeAuthorizationCodeStore : IAuthorizationCodeStore
{
    private readonly ConcurrentDictionary<string, AuthorizationCodePayload> _codes = new();

    public Task StoreAsync(string code, AuthorizationCodePayload payload, TimeSpan ttl, CancellationToken ct = default)
    {
        _codes[code] = payload;
        return Task.CompletedTask;
    }

    public Task<AuthorizationCodePayload?> ConsumeAsync(string code, CancellationToken ct = default)
        => Task.FromResult(_codes.TryRemove(code, out var p) ? p : null);
}

public sealed class FakeOidcRefreshStore : IOidcRefreshStore
{
    private sealed record Entry(Guid UserId, string ClientId, string Scope, Guid FamilyId);

    private readonly ConcurrentDictionary<string, Entry> _tokens = new();
    private readonly ConcurrentDictionary<string, Entry> _tombstones = new();
    public List<Guid> RevokedFamilies { get; } = new();

    public Task<(string Token, Guid FamilyId)> IssueAsync(OidcRefreshPayload payload, TimeSpan ttl, CancellationToken ct = default)
    {
        var token = $"ort-{Guid.NewGuid():N}";
        _tokens[token] = new Entry(payload.UserId, payload.ClientId, payload.Scope, payload.FamilyId);
        return Task.FromResult((token, payload.FamilyId));
    }

    public Task<OidcRotateResult> RotateAsync(string token, TimeSpan ttl, CancellationToken ct = default)
    {
        if (!_tokens.TryRemove(token, out var entry))
        {
            if (_tombstones.TryGetValue(token, out var stale))
            {
                RevokedFamilies.Add(stale.FamilyId);
                foreach (var k in _tokens.Where(t => t.Value.FamilyId == stale.FamilyId).Select(t => t.Key).ToList())
                    _tokens.TryRemove(k, out _);
                return Task.FromResult(OidcRotateResult.Reuse(
                    new OidcRefreshPayload(stale.UserId, stale.ClientId, stale.Scope, stale.FamilyId)));
            }
            return Task.FromResult(OidcRotateResult.Invalid);
        }

        _tombstones[token] = entry;
        var newToken = $"ort-{Guid.NewGuid():N}";
        _tokens[newToken] = entry;
        return Task.FromResult(OidcRotateResult.Rotated(
            new OidcRefreshPayload(entry.UserId, entry.ClientId, entry.Scope, entry.FamilyId), newToken));
    }

    public Task RevokeAsync(string token, CancellationToken ct = default)
    {
        _tokens.TryRemove(token, out _);
        return Task.CompletedTask;
    }

    public Task RevokeFamilyAsync(Guid familyId, CancellationToken ct = default)
    {
        RevokedFamilies.Add(familyId);
        foreach (var k in _tokens.Where(t => t.Value.FamilyId == familyId).Select(t => t.Key).ToList())
            _tokens.TryRemove(k, out _);
        return Task.CompletedTask;
    }

    public bool HasValidToken(string token) => _tokens.ContainsKey(token);
}

/// <summary>Captures OIDC token-mint arguments for assertions.</summary>
public sealed class CapturingTokenService : ITokenService
{
    public (string ClientId, string Scope)? LastOidcAccessArgs { get; private set; }
    public (string ClientId, string? Nonce)? LastIdTokenArgs { get; private set; }

    public AccessTokenResult CreateSessionToken(User user, Guid? sessionFamily = null)
        => new($"access-{user.Id}", 900, DateTimeOffset.UtcNow.AddMinutes(15));

    public AccessTokenResult CreateOidcAccessToken(User user, string clientId, string scope)
    {
        LastOidcAccessArgs = (clientId, scope);
        return new AccessTokenResult($"oidc-access-{clientId}-{user.Id}", 900, DateTimeOffset.UtcNow.AddMinutes(15));
    }

    public AccessTokenResult CreateIdToken(User user, string clientId, string? nonce)
    {
        LastIdTokenArgs = (clientId, nonce);
        return new AccessTokenResult($"id-token-{clientId}-{user.Id}", 900, DateTimeOffset.UtcNow.AddMinutes(15));
    }
}

public sealed class OidcHarness : IDisposable
{
    public SsoDbContext Db { get; }
    public Auth.FakeClock Clock { get; } = new(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
    public FakeAuthorizationCodeStore Codes { get; } = new();
    public FakeOidcRefreshStore OidcRefresh { get; } = new();
    public CapturingTokenService Tokens { get; } = new();
    public Auth.FakeAuditWriter Audit { get; } = new();
    public OidcService Service { get; }
    public ClientsService Clients { get; }

    public OidcHarness()
    {
        var options = new DbContextOptionsBuilder<SsoDbContext>()
            .UseInMemoryDatabase($"sso-oidc-test-{Guid.NewGuid():N}")
            .Options;
        Db = new SsoDbContext(options);
        Service = new OidcService(Db, Codes, OidcRefresh, Tokens, Audit,
            new OidcOptions { Issuer = "sso-test", WebBaseUrl = "http://localhost:5175" });
        Clients = new ClientsService(Db, Audit, Clock);
    }

    public Client SeedClient(
        string clientId = "agent-office",
        ClientType type = ClientType.Confidential,
        string? secret = "client-secret-1",
        string[]? redirectUris = null,
        string[]? scopes = null,
        bool skipConsent = true,
        bool active = true,
        string? launchUrl = null,
        bool requireAssignment = false)
    {
        var client = new Client
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            Name = clientId,
            Type = type,
            SecretHash = secret is null ? null : Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(secret))),
            RedirectUris = [.. redirectUris ?? ["https://office.example.com/auth/callback"]],
            AllowedScopes = [.. scopes ?? ["openid", "profile", "email", "offline_access"]],
            SkipConsent = skipConsent,
            LaunchUrl = launchUrl,
            RequireAssignment = requireAssignment,
            Active = active,
            CreatedAt = Clock.UtcNow,
        };
        Db.Clients.Add(client);
        Db.SaveChanges();
        return client;
    }

    public User SeedUser(string email = "user@example.com", bool admin = false)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            EmailVerifiedAt = Clock.UtcNow,
            IsSsoAdmin = admin,
            CreatedAt = Clock.UtcNow,
            UpdatedAt = Clock.UtcNow,
        };
        Db.Users.Add(user);
        Db.SaveChanges();
        return user;
    }

    public ClientAssignment SeedAssignment(User user, Client client, Guid? grantedBy = null)
    {
        var row = new ClientAssignment
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ClientId = client.Id,
            GrantedBy = grantedBy ?? Guid.NewGuid(),
            CreatedAt = Clock.UtcNow,
        };
        Db.ClientAssignments.Add(row);
        Db.SaveChanges();
        return row;
    }

    /// <summary>PKCE pair — the raw verifier goes to /oauth/token, the challenge to /oauth/authorize.</summary>
    public static (string Verifier, string Challenge) Pkce()
    {
        var verifier = Convert.ToBase64String(Guid.NewGuid().ToByteArray()
            .Concat(Guid.NewGuid().ToByteArray()).Concat(Guid.NewGuid().ToByteArray()).ToArray())
            .Replace('+', '-').Replace('/', '_').TrimEnd('='); // 48B → 64 chars (spec: 43–128)
        var challenge = Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.ASCII.GetBytes(verifier)))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return (verifier, challenge);
    }

    public void Dispose() => Db.Dispose();
}
