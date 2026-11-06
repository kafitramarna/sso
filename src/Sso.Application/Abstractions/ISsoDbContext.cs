using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Sso.Domain.Entities;

namespace Sso.Application.Abstractions;

public interface ISsoDbContext
{
    DbSet<User> Users { get; }
    DbSet<OneTimeToken> OneTimeTokens { get; }
    DbSet<AuditEvent> AuditEvents { get; }
    DbSet<Client> Clients { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
