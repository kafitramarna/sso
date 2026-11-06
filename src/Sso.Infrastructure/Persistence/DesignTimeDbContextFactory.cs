using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sso.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for `dotnet ef`. Connection string from SSO_DB,
/// falling back to the dev-compose defaults (deploy/docker-compose.dev.yml).
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SsoDbContext>
{
    public SsoDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SSO_DB")
            ?? "Host=localhost;Port=5434;Database=sso;Username=sso;Password=dev_password";

        var options = new DbContextOptionsBuilder<SsoDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new SsoDbContext(options);
    }
}
