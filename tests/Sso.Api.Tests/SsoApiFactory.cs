using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sso.Infrastructure.Persistence;

namespace Sso.Api.Tests;

/// <summary>
/// Api under test: InMemory EF + in-memory refresh store (no Redis) + ephemeral
/// RSA key (Testing env) + ConsoleNotifier. Seeded admin: admin@test.local.
/// </summary>
public class SsoApiFactory : WebApplicationFactory<Program>
{
    // One name per factory instance — AddDbContext rebuilds options per scope,
    // so an inline Guid would hand every scope a different (empty) store.
    private readonly string _dbName = $"sso-api-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Admin:ApiKey"] = "test-admin-key",
                ["SEED_ADMIN_EMAIL"] = "admin@test.local",
                ["SEED_ADMIN_PASSWORD"] = "admin-test-password",
                ["SEED_AGENTOFFICE_CLIENT_SECRET"] = "test-agent-secret",
                ["SEED_AGENTOFFICE_REDIRECT_URIS"] = "https://office.test/callback",
                ["Auth:Issuer"] = "sso-test",
                ["Auth:CookieSecure"] = "false",
                ["AllowSelfRegistration"] = "false",
            }));
        builder.ConfigureServices(services =>
            services.AddDbContext<SsoDbContext>(o =>
                o.UseInMemoryDatabase(_dbName)));
    }
}
