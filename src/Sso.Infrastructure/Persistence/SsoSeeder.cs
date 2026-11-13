using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sso.Domain.Entities;

namespace Sso.Infrastructure.Persistence;

/// <summary>
/// Idempotent bootstrap (KD-11): first admin from SEED_ADMIN_* — only when the
/// users table is empty, so restarts never re-seed. OIDC clients are managed
/// through the admin UI/API, not seeded (FR-C03 superseded).
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

        await db.SaveChangesAsync(ct);
    }
}
