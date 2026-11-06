using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sso.Infrastructure.Persistence;

/// <summary>
/// Boot-time DB init (FR-E02): migrate when relational + seed when empty
/// (KD-11). Runs as a hosted service so it uses the real service provider —
/// top-level Program code can resolve a deferred provider under
/// WebApplicationFactory and seed the wrong store.
/// </summary>
public sealed class SsoBootstrapHostedService(
    IServiceProvider services,
    IConfiguration configuration,
    ILogger<SsoBootstrapHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        if (scope.ServiceProvider.GetService<SsoDbContext>() is not { } db)
            return;

        if (db.Database.IsRelational())
            await db.Database.MigrateAsync(cancellationToken);
        await SsoSeeder.SeedAsync(db, configuration, logger, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
