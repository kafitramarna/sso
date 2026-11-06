using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Sso.Infrastructure.Persistence;
using StackExchange.Redis;

namespace Sso.Api.Health;

/// <summary>Ready-check dependencies (FR-E01): postgres, redis.</summary>
public sealed class PostgresHealthCheck(IServiceProvider sp) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await using var scope = sp.CreateAsyncScope();
            var db = scope.ServiceProvider.GetService<SsoDbContext>();
            if (db is null)
                return HealthCheckResult.Unhealthy("not configured");
            var ok = await db.Database.CanConnectAsync(ct);
            return ok
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("cannot connect");
        }
        catch (Exception ex) { return HealthCheckResult.Unhealthy(exception: ex); }
    }
}

public sealed class RedisHealthCheck(IServiceProvider sp) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var mux = sp.GetService<IConnectionMultiplexer>();
            if (mux is null)
                return HealthCheckResult.Unhealthy("not configured");
            var rtt = await mux.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy($"rtt {rtt.TotalMilliseconds:0} ms");
        }
        catch (Exception ex) { return HealthCheckResult.Unhealthy(exception: ex); }
    }
}
