using System.Collections.Concurrent;
using System.Security.Cryptography;
using Sso.Application.Abstractions;

namespace Sso.Infrastructure.Auth;

/// <summary>
/// Process-memory fallback when Redis is not configured — sessions do not
/// survive restart. Same family-rotation semantics as the Redis store (KD-03).
/// </summary>
public sealed class InMemoryRefreshTokenStore : IRefreshTokenStore
{
    private sealed record Entry(
        Guid UserId, Guid FamilyId, SessionMeta Meta,
        DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, Entry> _tokens = new();      // hash → entry
    private readonly ConcurrentDictionary<string, Entry> _tombstones = new();  // rotated hash → entry
    private readonly ConcurrentDictionary<Guid, byte> _liveFamilies = new();   // familyId → dummy

    public Task<(string Token, Guid FamilyId)> IssueAsync(
        Guid userId, Guid? familyId, SessionMeta meta, TimeSpan ttl, CancellationToken ct = default)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var family = familyId ?? Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var created = familyId is not null && _tokens.Values.FirstOrDefault(e => e.FamilyId == familyId) is { } prev
            ? prev.CreatedAt : now;
        _tokens[Hash(token)] = new Entry(userId, family, meta, created, now, now + ttl);
        _liveFamilies[family] = 0;
        return Task.FromResult((token, family));
    }

    public Task<RotateResult> RotateAsync(string token, TimeSpan ttl, CancellationToken ct = default)
    {
        var hash = Hash(token);
        if (!_tokens.TryRemove(hash, out var entry) || entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            if (_tombstones.TryGetValue(hash, out var stale))
            {
                RevokeFamily(stale.FamilyId);
                return Task.FromResult(RotateResult.Reuse(
                    new RefreshPayload(stale.UserId, stale.FamilyId, stale.Meta)));
            }
            return Task.FromResult(RotateResult.Invalid);
        }

        _tombstones[hash] = entry;
        var newToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        _tokens[Hash(newToken)] = entry with { LastSeenAt = DateTimeOffset.UtcNow };
        return Task.FromResult(RotateResult.Rotated(
            new RefreshPayload(entry.UserId, entry.FamilyId, entry.Meta), newToken));
    }

    public Task<RefreshPayload?> ResolveAsync(string token, CancellationToken ct = default)
        => Task.FromResult(_tokens.TryGetValue(Hash(token), out var e) && e.ExpiresAt > DateTimeOffset.UtcNow
            ? new RefreshPayload(e.UserId, e.FamilyId, e.Meta)
            : null);

    public Task RevokeFamilyAsync(Guid familyId, CancellationToken ct = default)
    {
        RevokeFamily(familyId);
        return Task.CompletedTask;
    }

    public Task RevokeAllAsync(Guid userId, Guid? exceptFamilyId = null, CancellationToken ct = default)
    {
        foreach (var familyId in _tokens.Values.Where(e => e.UserId == userId).Select(e => e.FamilyId).Distinct())
            if (familyId != exceptFamilyId)
                RevokeFamily(familyId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SessionInfo>> ListSessionsAsync(Guid userId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        IReadOnlyList<SessionInfo> sessions = _tokens.Values
            .Where(e => e.UserId == userId && e.ExpiresAt > now)
            .GroupBy(e => e.FamilyId)
            .Select(g => new SessionInfo(g.Key, g.First().Meta.UserAgent, g.First().Meta.Ip,
                g.Min(e => e.CreatedAt), g.Max(e => e.LastSeenAt)))
            .ToList();
        return Task.FromResult(sessions);
    }

    private void RevokeFamily(Guid familyId)
    {
        _liveFamilies.TryRemove(familyId, out _);
        foreach (var kv in _tokens.Where(t => t.Value.FamilyId == familyId).ToList())
            _tokens.TryRemove(kv.Key, out _);
    }

    private static string Hash(string token)
        => Convert.ToHexStringLower(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
}
