using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sso.Application.Abstractions;
using Sso.Application.Errors;
using Sso.Domain.Entities;
using Sso.Domain.Enums;

namespace Sso.Application.Oidc;

/// <summary>
/// Per-user app assignments (KD-14): which registered clients appear on a
/// user's /dashboard launcher — and, for clients with require_assignment,
/// whether /oauth/authorize may issue them a code at all. sso_admin sees and
/// may launch every launchable client without explicit rows.
/// </summary>
public sealed class AssignmentsService(ISsoDbContext db, IAuditWriter audit, IClock clock)
{
    /// <summary>GET /v1/me/apps — assigned launchable clients; admins get all.</summary>
    public async Task<IReadOnlyList<AppDto>> ListForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw ApiError.Unauthenticated();

        var clients = user.IsSsoAdmin
            ? await db.Clients.AsNoTracking()
                .Where(c => c.Active && c.LaunchUrl != null)
                .ToListAsync(ct)
            : await db.Clients.AsNoTracking()
                .Where(c => c.Active && c.LaunchUrl != null
                    && db.ClientAssignments.Any(a => a.UserId == userId && a.ClientId == c.Id))
                .ToListAsync(ct);

        return clients.OrderBy(c => c.Name)
            .Select(c => new AppDto(c.ClientId, c.Name, c.Description, c.LaunchUrl))
            .ToList();
    }

    /// <summary>Admin view — the assigned client_id strings for a user.</summary>
    public async Task<IReadOnlyList<string>> ClientIdsForUserAsync(Guid userId, CancellationToken ct = default)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId, ct))
            throw ApiError.NotFound("User not found.");

        return await db.ClientAssignments.AsNoTracking()
            .Where(a => a.UserId == userId)
            .Join(db.Clients, a => a.ClientId, c => c.Id, (_, c) => c.ClientId)
            .OrderBy(id => id)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Replaces the user's assignment set (idempotent): rows not in the
    /// request are revoked, new client_ids are granted, the rest untouched.
    /// Every grant/revoke is audited.
    /// </summary>
    public async Task<IReadOnlyList<string>> SetForUserAsync(
        Guid userId, SetUserAppsRequest req, Guid actorId,
        IPAddress? ip, string? userAgent, CancellationToken ct = default)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId, ct))
            throw ApiError.NotFound("User not found.");

        var wanted = (req.ClientIds ?? [])
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct()
            .ToList();
        var clients = await db.Clients.Where(c => wanted.Contains(c.ClientId)).ToListAsync(ct);
        var unknown = wanted.Except(clients.Select(c => c.ClientId)).ToList();
        if (unknown.Count > 0)
            throw ApiError.Validation("Unknown client_id values.", new { errors = unknown });

        var existing = await db.ClientAssignments.Where(a => a.UserId == userId).ToListAsync(ct);
        var wantedIds = clients.Select(c => c.Id).ToHashSet();
        var revoked = existing.Where(a => !wantedIds.Contains(a.ClientId)).ToList();
        var granted = clients.Where(c => existing.All(a => a.ClientId != c.Id)).ToList();

        // InMemory (tests) has no transactions — the set still lands atomically
        // enough via a single SaveChanges on the relational provider.
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct)
            : null;

        var now = clock.UtcNow;
        db.ClientAssignments.RemoveRange(revoked);
        db.ClientAssignments.AddRange(granted.Select(c => new ClientAssignment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ClientId = c.Id,
            GrantedBy = actorId,
            CreatedAt = now,
        }));
        await db.SaveChangesAsync(ct);
        if (tx is not null)
            await tx.CommitAsync(ct);

        foreach (var c in granted)
            await audit.WriteAsync(AuditEvents.AppGranted, userId, c.Id,
                ip: ip, userAgent: userAgent, metadata: new { actor = actorId }, ct: ct);
        foreach (var a in revoked)
            await audit.WriteAsync(AuditEvents.AppRevoked, userId, a.ClientId,
                ip: ip, userAgent: userAgent, metadata: new { actor = actorId }, ct: ct);

        return wanted;
    }
}
