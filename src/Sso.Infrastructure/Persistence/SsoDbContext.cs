using Microsoft.EntityFrameworkCore;

namespace Sso.Infrastructure.Persistence;

/// <summary>SSO persistence. Entity sets are added per docs/01-PRD §9 (Fase 1).</summary>
public sealed class SsoDbContext(DbContextOptions<SsoDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(SsoDbContext).Assembly);
}
