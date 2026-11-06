using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Sso.Infrastructure.Persistence;

public static class DependencyInjection
{
    /// <summary>
    /// Registers EF Core (Npgsql) and the Redis multiplexer. Both are skipped when
    /// their connection string is absent — health checks then report "not
    /// configured" instead of the app failing to boot (KD-05, KD-12).
    /// </summary>
    public static IServiceCollection AddSsoPersistence(
        this IServiceCollection services, IConfiguration configuration)
    {
        var cs = configuration["SSO_DB"] ?? configuration.GetConnectionString("Default");
        if (!string.IsNullOrEmpty(cs))
            services.AddDbContext<SsoDbContext>(options =>
                options.UseNpgsql(cs).UseSnakeCaseNamingConvention());

        var redis = configuration["SSO_REDIS"] ?? configuration.GetConnectionString("Redis");
        if (!string.IsNullOrEmpty(redis))
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redis));

        return services;
    }
}
