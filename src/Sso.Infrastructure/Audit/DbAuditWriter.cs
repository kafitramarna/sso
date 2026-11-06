using System.Net;
using System.Text.Json;
using Sso.Application.Abstractions;
using Sso.Domain.Entities;
using Sso.Infrastructure.Persistence;

namespace Sso.Infrastructure.Audit;

/// <summary>Appends audit_events rows (FR-D02). Shares the request-scoped DbContext.</summary>
public sealed class DbAuditWriter(SsoDbContext db) : IAuditWriter
{
    public async Task WriteAsync(
        string eventName, Guid? userId = null, Guid? clientId = null,
        IPAddress? ip = null, string? userAgent = null, object? metadata = null,
        CancellationToken ct = default)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Event = eventName,
            UserId = userId,
            ClientId = clientId,
            Ip = ip,
            UserAgent = userAgent is { Length: > 500 } ? userAgent[..500] : userAgent,
            Metadata = metadata is null ? null : JsonSerializer.Serialize(metadata),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }
}
