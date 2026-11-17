namespace Sso.Domain.Entities;

/// <summary>
/// Per-user grant to a launchable app (KD-14). Admin assigns a user to an OIDC
/// client; the row both feeds the /dashboard launcher and gates
/// /oauth/authorize when the client sets require_assignment.
/// </summary>
public sealed class ClientAssignment
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ClientId { get; set; }

    /// <summary>Admin user id that granted the assignment.</summary>
    public Guid GrantedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User? User { get; set; }
    public Client? Client { get; set; }
}
