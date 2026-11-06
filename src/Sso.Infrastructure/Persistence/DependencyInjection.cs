using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sso.Application.Abstractions;
using Sso.Application.Auth;
using Sso.Application.Oidc;
using Sso.Domain.Entities;
using Sso.Infrastructure.Audit;
using Sso.Infrastructure.Auth;
using Sso.Infrastructure.Notifications;
using Sso.Infrastructure.Oidc;
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

    /// <summary>Auth/session services per KD-03, KD-07, KD-10.</summary>
    public static IServiceCollection AddSsoServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ISsoDbContext>(sp =>
            sp.GetService<SsoDbContext>()
            ?? throw new InvalidOperationException("SsoDbContext unavailable — SSO_DB not configured."));
        services.AddScoped<IAuditWriter>(sp =>
        {
            var db = sp.GetService<SsoDbContext>();
            return db is null
                ? new NullAuditWriter()
                : (IAuditWriter)new DbAuditWriter(db);
        });

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton(new AuthOptions
        {
            AllowSelfRegistration = configuration.GetValue("AllowSelfRegistration", false),
            WebBaseUrl = configuration["App:WebBaseUrl"] ?? "http://localhost:5175",
        });

        services.AddSingleton<Rs256JwtTokenService>();
        services.AddSingleton<ITokenService>(sp => sp.GetRequiredService<Rs256JwtTokenService>());

        services.AddSingleton<IRefreshTokenStore>(sp =>
            sp.GetService<IConnectionMultiplexer>() is { } mux
                ? new RedisRefreshTokenStore(mux)
                : new InMemoryRefreshTokenStore());

        // OIDC provider stores — dedicated `oidc:` keyspace so OIDC grants can
        // never be exchanged for SSO session tokens (KD-01).
        services.AddSingleton<IAuthorizationCodeStore>(sp =>
            sp.GetService<IConnectionMultiplexer>() is { } mux
                ? (IAuthorizationCodeStore)new RedisAuthorizationCodeStore(mux)
                : new InMemoryAuthorizationCodeStore());
        services.AddSingleton<IOidcRefreshStore>(sp =>
            sp.GetService<IConnectionMultiplexer>() is { } mux
                ? (IOidcRefreshStore)new RedisOidcRefreshStore(mux)
                : new InMemoryOidcRefreshStore());
        services.AddSingleton(new OidcOptions
        {
            Issuer = configuration["Auth:Issuer"] ?? "sso",
            WebBaseUrl = configuration["App:WebBaseUrl"] ?? "http://localhost:5175",
            CodeTtl = TimeSpan.FromSeconds(configuration.GetValue("Oidc:CodeTtlSeconds", 90)),
            RefreshTokenTtl = TimeSpan.FromDays(configuration.GetValue("Oidc:RefreshTokenDays", 30)),
        });
        services.AddScoped<OidcService>();
        services.AddScoped<ClientsService>();

        services.AddScoped<INotifier>(sp =>
            !string.IsNullOrWhiteSpace(configuration["Email:Smtp:Host"])
                ? (INotifier)ActivatorUtilities.CreateInstance<SmtpNotifier>(sp)
                : ActivatorUtilities.CreateInstance<ConsoleNotifier>(sp));

        services.AddScoped<AuthService>();
        services.AddHostedService<SsoBootstrapHostedService>();
        return services;
    }

    private sealed class NullAuditWriter : IAuditWriter
    {
        public Task WriteAsync(string eventName, Guid? userId = null, Guid? clientId = null,
            System.Net.IPAddress? ip = null, string? userAgent = null, object? metadata = null,
            CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
