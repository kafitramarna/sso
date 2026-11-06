using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sso.Application.Abstractions;
using StackExchange.Redis;

namespace Sso.Infrastructure.Auth;

/// <summary>
/// Family-based rotating refresh tokens in Redis (KD-03).
///
///   sso:rt:{sha256(token)}   → session entry JSON (TTL = refresh TTL)
///   sso:used:{sha256(token)} → tombstone of a rotated token → entry JSON (TTL)
///   sso:fam:{familyId}       → family record JSON (TTL)
///   sso:fam:{familyId}:tok   → set of live token hashes in the family
///   sso:u:{userId}           → set of the user's family ids
///
/// Rotation consumes the token and leaves a tombstone; presenting a tombstoned
/// token = reuse = the whole family is revoked (theft detection).
/// </summary>
public sealed class RedisRefreshTokenStore(IConnectionMultiplexer redis) : IRefreshTokenStore
{
    private sealed record Entry(
        Guid UserId, Guid FamilyId, string? UserAgent, string? Ip,
        DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt);

    private IDatabase Db => redis.GetDatabase();

    public async Task<(string Token, Guid FamilyId)> IssueAsync(
        Guid userId, Guid? familyId, SessionMeta meta, TimeSpan ttl, CancellationToken ct = default)
    {
        var token = NewToken();
        var hash = Hash(token);
        var family = familyId ?? Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Re-issue inside an existing family keeps the family's original CreatedAt.
        var familyCreated = now;
        if (familyId is not null)
        {
            var existing = await Db.StringGetAsync(FamilyKey(family));
            if (!existing.IsNullOrEmpty
                && JsonSerializer.Deserialize<Entry>(existing.ToString()) is { } prev)
                familyCreated = prev.CreatedAt;
        }

        var entry = new Entry(userId, family, meta.UserAgent, meta.Ip, familyCreated, now);
        var batch = Db.CreateBatch();
        var t1 = batch.StringSetAsync(TokenKey(hash), Json(entry), ttl);
        var t2 = batch.StringSetAsync(FamilyKey(family), Json(entry), ttl);
        var t3 = batch.SetAddAsync(FamilyTokensKey(family), hash);
        var t4 = batch.KeyExpireAsync(FamilyTokensKey(family), ttl);
        var t5 = batch.SetAddAsync(UserFamiliesKey(userId), family.ToString());
        var t6 = batch.KeyExpireAsync(UserFamiliesKey(userId), ttl);
        batch.Execute();
        await Task.WhenAll(t1, t2, t3, t4, t5, t6);
        return (token, family);
    }

    public async Task<RotateResult> RotateAsync(string token, TimeSpan ttl, CancellationToken ct = default)
    {
        var hash = Hash(token);
        var raw = await Db.StringGetDeleteAsync(TokenKey(hash));

        if (raw.IsNullOrEmpty)
        {
            var tomb = await Db.StringGetAsync(TombstoneKey(hash));
            if (tomb.IsNullOrEmpty || JsonSerializer.Deserialize<Entry>(tomb.ToString()) is not { } stale)
                return RotateResult.Invalid;

            await RevokeFamilyAsync(stale.FamilyId, ct);
            return RotateResult.Reuse(ToPayload(stale));
        }

        if (JsonSerializer.Deserialize<Entry>(raw.ToString()) is not { } entry)
            return RotateResult.Invalid;

        // Tombstone first (fail-closed for concurrent rotates), then mint the next token.
        await Db.StringSetAsync(TombstoneKey(hash), raw.ToString(), ttl);
        var (newToken, _) = await IssueAsync(entry.UserId, entry.FamilyId,
            new SessionMeta(entry.UserAgent, entry.Ip), ttl, ct);
        await Db.SetRemoveAsync(FamilyTokensKey(entry.FamilyId), hash);
        return RotateResult.Rotated(ToPayload(entry), newToken);
    }

    public async Task<RefreshPayload?> ResolveAsync(string token, CancellationToken ct = default)
    {
        var raw = await Db.StringGetAsync(TokenKey(Hash(token)));
        return raw.IsNullOrEmpty || JsonSerializer.Deserialize<Entry>(raw.ToString()) is not { } e
            ? null : ToPayload(e);
    }

    public async Task RevokeFamilyAsync(Guid familyId, CancellationToken ct = default)
    {
        var raw = await Db.StringGetAsync(FamilyKey(familyId));
        var tokenHashes = await Db.SetMembersAsync(FamilyTokensKey(familyId));

        var batch = Db.CreateBatch();
        var tasks = new List<Task>
        {
            batch.KeyDeleteAsync(FamilyKey(familyId)),
            batch.KeyDeleteAsync(FamilyTokensKey(familyId)),
        };
        foreach (var h in tokenHashes)
            tasks.Add(batch.KeyDeleteAsync(TokenKey(h.ToString())));
        if (!raw.IsNullOrEmpty && JsonSerializer.Deserialize<Entry>(raw.ToString()) is { } e)
            tasks.Add(batch.SetRemoveAsync(UserFamiliesKey(e.UserId), familyId.ToString()));
        batch.Execute();
        await Task.WhenAll(tasks);
    }

    public async Task RevokeAllAsync(Guid userId, Guid? exceptFamilyId = null, CancellationToken ct = default)
    {
        var families = await Db.SetMembersAsync(UserFamiliesKey(userId));
        foreach (var f in families)
        {
            if (Guid.TryParse(f.ToString(), out var familyId) && familyId != exceptFamilyId)
                await RevokeFamilyAsync(familyId, ct);
        }
    }

    public async Task<IReadOnlyList<SessionInfo>> ListSessionsAsync(Guid userId, CancellationToken ct = default)
    {
        var families = await Db.SetMembersAsync(UserFamiliesKey(userId));
        var sessions = new List<SessionInfo>();
        foreach (var f in families)
        {
            var raw = await Db.StringGetAsync(FamilyKey(Guid.Parse(f.ToString())));
            if (!raw.IsNullOrEmpty && JsonSerializer.Deserialize<Entry>(raw.ToString()) is { } e)
                sessions.Add(new SessionInfo(e.FamilyId, e.UserAgent, e.Ip, e.CreatedAt, e.LastSeenAt));
        }
        return sessions;
    }

    private static RefreshPayload ToPayload(Entry e)
        => new(e.UserId, e.FamilyId, new SessionMeta(e.UserAgent, e.Ip));

    private static string NewToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string Hash(string token)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Json(Entry e) => JsonSerializer.Serialize(e);
    private static string TokenKey(string hash) => $"sso:rt:{hash}";
    private static string TombstoneKey(string hash) => $"sso:used:{hash}";
    private static string FamilyKey(Guid family) => $"sso:fam:{family}";
    private static string FamilyTokensKey(Guid family) => $"sso:fam:{family}:tok";
    private static string UserFamiliesKey(Guid userId) => $"sso:u:{userId}";
}
