using Microsoft.EntityFrameworkCore;
using Sso.Application.Abstractions;
using Sso.Domain.Entities;

namespace Sso.Infrastructure.Persistence;

/// <summary>SSO persistence — entity model per docs/01-PRD §9.</summary>
public sealed class SsoDbContext(DbContextOptions<SsoDbContext> options) : DbContext(options), ISsoDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<OneTimeToken> OneTimeTokens => Set<OneTimeToken>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<Client> Clients => Set<Client>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("citext");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SsoDbContext).Assembly);
    }
}
