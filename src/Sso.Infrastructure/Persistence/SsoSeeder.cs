using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sso.Domain.Entities;
using Sso.Domain.Enums;

namespace Sso.Infrastructure.Persistence;

/// <summary>
/// Idempotent bootstrap (KD-11): first admin from SEED_ADMIN_* and the
/// agent-office confidential client from SEED_AGENTOFFICE_* — each only when
/// its table is empty, so restarts never re-seed (FR-C03).
/// </summary>
public static class SsoSeeder
{
    public static async Task SeedAsync(
        SsoDbContext db, IConfiguration config, ILogger logger, CancellationToken ct = default)
    {
        if (!await db.Users.AnyAsync(ct))
        {
            var email = config["SEED_ADMIN_EMAIL"];
            var password = config["SEED_ADMIN_PASSWORD"];
            if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password))
            {
                var now = DateTimeOffset.UtcNow;
                var admin = new User
                {
                    Id = Guid.NewGuid(),
                    Email = email.Trim().ToLowerInvariant(),
                    DisplayName = "SSO Admin",
                    IsSsoAdmin = true,
                    EmailVerifiedAt = now,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                admin.PasswordHash = new PasswordHasher<User>().HashPassword(admin, password);
                db.Users.Add(admin);
                logger.LogInformation("seeded sso_admin {Email}", admin.Email);
            }
            else
            {
                logger.LogWarning("users table empty and SEED_ADMIN_* not set — no admin seeded");
            }
        }

        if (!await db.Clients.AnyAsync(ct))
        {
            var secret = config["SEED_AGENTOFFICE_CLIENT_SECRET"];
            var redirectUrisRaw = config["SEED_AGENTOFFICE_REDIRECT_URIS"];
            if (!string.IsNullOrWhiteSpace(secret) && !string.IsNullOrWhiteSpace(redirectUrisRaw))
            {
                var redirectUris = redirectUrisRaw
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                db.Clients.Add(new Client
                {
                    Id = Guid.NewGuid(),
                    ClientId = config["SEED_AGENTOFFICE_CLIENT_ID"] ?? "agent-office",
                    Name = "Agent Office",
                    Type = ClientType.Confidential,
                    SecretHash = HashSecret(secret),
                    RedirectUris = [.. redirectUris],
                    // offline_access → agent-office gets rotating refresh
                    // tokens for silent renew (KD-01/KD-08).
                    AllowedScopes = ["openid", "profile", "email", "offline_access"],
                    SkipConsent = true,
                    Active = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                logger.LogInformation("seeded oidc client agent-office");
            }
            else if (!string.IsNullOrWhiteSpace(secret) || !string.IsNullOrWhiteSpace(redirectUrisRaw))
            {
                logger.LogWarning(
                    "clients table empty but SEED_AGENTOFFICE_* incomplete — no client seeded");
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Client secrets are high-entropy — SHA-256 hash at rest (FR-C02).</summary>
    public static string HashSecret(string secret)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
