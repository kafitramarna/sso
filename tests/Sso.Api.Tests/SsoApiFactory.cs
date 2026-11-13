using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sso.Domain.Entities;
using Sso.Domain.Enums;
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
                ["Auth:Issuer"] = "sso-test",
                ["Auth:CookieSecure"] = "false",
                ["AllowSelfRegistration"] = "false",
            }));
        builder.ConfigureServices(services =>
            services.AddDbContext<SsoDbContext>(o =>
                o.UseInMemoryDatabase(_dbName)));
    }

    /// <summary>
    /// Test clients are inserted directly — production seeds nothing but the
    /// first admin (clients come from the admin UI/API).
    /// </summary>
    public void SeedAgentOfficeClient()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SsoDbContext>();
        if (db.Clients.Any(c => c.ClientId == "agent-office"))
            return;
        db.Clients.Add(new Client
        {
            Id = Guid.NewGuid(),
            ClientId = "agent-office",
            Name = "Agent Office",
            Type = ClientType.Confidential,
            SecretHash = Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(
                    Encoding.UTF8.GetBytes("test-agent-secret"))),
            RedirectUris = ["https://office.test/callback"],
            AllowedScopes = ["openid", "profile", "email", "offline_access"],
            SkipConsent = true,
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
    }
}
