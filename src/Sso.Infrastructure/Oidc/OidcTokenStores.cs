using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sso.Application.Abstractions;
using StackExchange.Redis;

namespace Sso.Infrastructure.Oidc;

/// <summary>
/// Authorization codes in Redis — `oidc:code:{code}` JSON payload, TTL 90 s
/// (FR-B03). Consume is atomic GETDEL, so a replayed code can never succeed.
/// </summary>
public sealed class RedisAuthorizationCodeStore(IConnectionMultiplexer redis) : IAuthorizationCodeStore
{
    private IDatabase Db => redis.GetDatabase();

    public Task StoreAsync(string code, AuthorizationCodePayload payload, TimeSpan ttl, CancellationToken ct = default)
        => Db.StringSetAsync($"oidc:code:{code}", JsonSerializer.Serialize(payload), ttl);

    public async Task<AuthorizationCodePayload?> ConsumeAsync(string code, CancellationToken ct = default)
    {
        var raw = await Db.StringGetDeleteAsync($"oidc:code:{code}");
        return raw.IsNullOrEmpty ? null : JsonSerializer.Deserialize<AuthorizationCodePayload>(raw.ToString());
    }
}

/// <summary>Process-memory fallback when Redis is absent (dev/tests).</summary>
public sealed class InMemoryAuthorizationCodeStore : IAuthorizationCodeStore
{
    private readonly ConcurrentDictionary<string, (AuthorizationCodePayload Payload, DateTimeOffset ExpiresAt)> _codes = new();

    public Task StoreAsync(string code, AuthorizationCodePayload payload, TimeSpan ttl, CancellationToken ct = default)
    {
        _codes[code] = (payload, DateTimeOffset.UtcNow + ttl);
        return Task.CompletedTask;
    }

    public Task<AuthorizationCodePayload?> ConsumeAsync(string code, CancellationToken ct = default)
    {
        if (!_codes.TryRemove(code, out var e) || e.ExpiresAt <= DateTimeOffset.UtcNow)
            return Task.FromResult<AuthorizationCodePayload?>(null);
        return Task.FromResult<AuthorizationCodePayload?>(e.Payload);
    }
}

/// <summary>
/// OIDC refresh tokens — same family-rotation + tombstone semantics as the SSO
/// session store but in the `oidc:` keyspace, bound to (user, client, scope)
/// so an OIDC refresh token can never become an SSO session (KD-01/KD-03).
///
///   oidc:rt:{sha256(token)}   → entry JSON (TTL)
///   oidc:used:{sha256(token)} → tombstone (reuse ⇒ family revoked)
///   oidc:fam:{familyId}       → family record (TTL)
///   oidc:fam:{familyId}:tok   → live token hashes in the family
/// </summary>
public sealed class RedisOidcRefreshStore(IConnectionMultiplexer redis) : IOidcRefreshStore
{
    private sealed record Entry(Guid UserId, string ClientId, string Scope, Guid FamilyId);

    private IDatabase Db => redis.GetDatabase();

    public async Task<(string Token, Guid FamilyId)> IssueAsync(
        OidcRefreshPayload payload, TimeSpan ttl, CancellationToken ct = default)
    {
        var token = NewToken();
        var hash = Hash(token);
        var entry = new Entry(payload.UserId, payload.ClientId, payload.Scope, payload.FamilyId);

        var batch = Db.CreateBatch();
        var t1 = batch.StringSetAsync($"oidc:rt:{hash}", Json(entry), ttl);
        var t2 = batch.StringSetAsync($"oidc:fam:{payload.FamilyId}", Json(entry), ttl);
        var t3 = batch.SetAddAsync($"oidc:fam:{payload.FamilyId}:tok", hash);
        var t4 = batch.KeyExpireAsync($"oidc:fam:{payload.FamilyId}:tok", ttl);
        batch.Execute();
        await Task.WhenAll(t1, t2, t3, t4);
        return (token, payload.FamilyId);
    }

    public async Task<OidcRotateResult> RotateAsync(string token, TimeSpan ttl, CancellationToken ct = default)
    {
        var hash = Hash(token);
        var raw = await Db.StringGetDeleteAsync($"oidc:rt:{hash}");

        if (raw.IsNullOrEmpty)
        {
            var tomb = await Db.StringGetAsync($"oidc:used:{hash}");
            if (tomb.IsNullOrEmpty || JsonSerializer.Deserialize<Entry>(tomb.ToString()) is not { } stale)
                return OidcRotateResult.Invalid;

            await RevokeFamilyAsync(stale.FamilyId, ct);
            return OidcRotateResult.Reuse(new OidcRefreshPayload(
                stale.UserId, stale.ClientId, stale.Scope, stale.FamilyId));
        }

        if (JsonSerializer.Deserialize<Entry>(raw.ToString()) is not { } entry)
            return OidcRotateResult.Invalid;

        await Db.StringSetAsync($"oidc:used:{hash}", raw.ToString(), ttl);
        var (newToken, _) = await IssueAsync(
            new OidcRefreshPayload(entry.UserId, entry.ClientId, entry.Scope, entry.FamilyId), ttl, ct);
        await Db.SetRemoveAsync($"oidc:fam:{entry.FamilyId}:tok", hash);
        return OidcRotateResult.Rotated(
            new OidcRefreshPayload(entry.UserId, entry.ClientId, entry.Scope, entry.FamilyId), newToken);
    }

    public Task RevokeAsync(string token, CancellationToken ct = default)
        => Db.KeyDeleteAsync($"oidc:rt:{Hash(token)}");

    public async Task RevokeFamilyAsync(Guid familyId, CancellationToken ct = default)
    {
        var tokenHashes = await Db.SetMembersAsync($"oidc:fam:{familyId}:tok");
        var batch = Db.CreateBatch();
        var tasks = new List<Task>
        {
            batch.KeyDeleteAsync($"oidc:fam:{familyId}"),
            batch.KeyDeleteAsync($"oidc:fam:{familyId}:tok"),
        };
        foreach (var h in tokenHashes)
            tasks.Add(batch.KeyDeleteAsync($"oidc:rt:{h}"));
        batch.Execute();
        await Task.WhenAll(tasks);
    }

    private static string Json(Entry e) => JsonSerializer.Serialize(e);
    private static string NewToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    private static string Hash(string token)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

/// <summary>Process-memory fallback when Redis is absent (dev/tests).</summary>
public sealed class InMemoryOidcRefreshStore : IOidcRefreshStore
{
    private sealed record Entry(
        Guid UserId, string ClientId, string Scope, Guid FamilyId, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, Entry> _tokens = new();
    private readonly ConcurrentDictionary<string, Entry> _tombstones = new();

    public Task<(string Token, Guid FamilyId)> IssueAsync(
        OidcRefreshPayload payload, TimeSpan ttl, CancellationToken ct = default)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        _tokens[Hash(token)] = new Entry(payload.UserId, payload.ClientId, payload.Scope,
            payload.FamilyId, DateTimeOffset.UtcNow + ttl);
        return Task.FromResult((token, payload.FamilyId));
    }

    public Task<OidcRotateResult> RotateAsync(string token, TimeSpan ttl, CancellationToken ct = default)
    {
        var hash = Hash(token);
        if (!_tokens.TryRemove(hash, out var entry) || entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            if (_tombstones.TryGetValue(hash, out var stale))
            {
                RevokeFamily(stale.FamilyId);
                return Task.FromResult(OidcRotateResult.Reuse(
                    new OidcRefreshPayload(stale.UserId, stale.ClientId, stale.Scope, stale.FamilyId)));
            }
            return Task.FromResult(OidcRotateResult.Invalid);
        }

        _tombstones[hash] = entry;
        var newToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        _tokens[Hash(newToken)] = entry with { ExpiresAt = DateTimeOffset.UtcNow + ttl };
        return Task.FromResult(OidcRotateResult.Rotated(
            new OidcRefreshPayload(entry.UserId, entry.ClientId, entry.Scope, entry.FamilyId), newToken));
    }

    public Task RevokeAsync(string token, CancellationToken ct = default)
    {
        _tokens.TryRemove(Hash(token), out _);
        return Task.CompletedTask;
    }

    public Task RevokeFamilyAsync(Guid familyId, CancellationToken ct = default)
    {
        RevokeFamily(familyId);
        return Task.CompletedTask;
    }

    private void RevokeFamily(Guid familyId)
    {
        foreach (var kv in _tokens.Where(t => t.Value.FamilyId == familyId).ToList())
            _tokens.TryRemove(kv.Key, out _);
    }

    private static string Hash(string token)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
