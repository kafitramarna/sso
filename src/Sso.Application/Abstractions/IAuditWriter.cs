using System.Net;

namespace Sso.Application.Abstractions;

/// <summary>Appends to audit_events (FR-D02) — every auth mutation is logged.</summary>
public interface IAuditWriter
{
    Task WriteAsync(
        string eventName,
        Guid? userId = null,
        Guid? clientId = null,
        IPAddress? ip = null,
        string? userAgent = null,
        object? metadata = null,
        CancellationToken ct = default);
}
