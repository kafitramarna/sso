using System.Net;

namespace Sso.Domain.Entities;

/// <summary>Append-only security audit trail (FR-D02). Never updated or deleted.</summary>
public sealed class AuditEvent
{
    public long Id { get; set; }
    public Guid? UserId { get; set; }
    public Guid? ClientId { get; set; }
    public required string Event { get; set; }
    public IPAddress? Ip { get; set; }
    public string? UserAgent { get; set; }
    /// <summary>JSON payload (jsonb column). String keeps EF InMemory test providers happy.</summary>
    public string? Metadata { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
