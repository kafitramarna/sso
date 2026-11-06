using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Sso.Api.Http;
using Sso.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHttpClient();
builder.Services.AddHealthChecks()
    .AddCheck<Sso.Api.Health.PostgresHealthCheck>("postgres")
    .AddCheck<Sso.Api.Health.RedisHealthCheck>("redis");

// API contract uses snake_case JSON everywhere (docs/01-PRD §10).
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
});

builder.Services.AddSsoPersistence(builder.Configuration);

// Rate limiting (FR-E04). "auth" applies to credential endpoints (Fase 1+);
// defined up-front so limiter behaviour is one place, like plotsure WS-6.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (ctx, ct) =>
    {
        var retryAfter = ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var ra)
            ? (int)ra.TotalSeconds
            : 60;
        ctx.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString();
        await ctx.HttpContext.Response.WriteAsJsonAsync(new
        {
            success = false,
            error = new
            {
                code = "rate_limited",
                message = $"Too many requests. Retry after {retryAfter} seconds.",
                details = (object?)null,
            },
            meta = ApiEnvelope.Meta(),
        }, cancellationToken: ct);
    };

    static string IpKey(HttpContext c)
        => c.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    options.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        IpKey(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20, Window = TimeSpan.FromSeconds(60), QueueLimit = 0,
        }));
});

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(builder.Configuration["Cors:Origins"]?.Split(',') ?? ["http://localhost:5175"])
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

var app = builder.Build();

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseCors();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("/v1/openapi.json");
}

// /health/live = process up; /health/ready = all dependencies reachable (FR-E01).
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString().ToLowerInvariant(),
            dependencies = report.Entries.ToDictionary(
                e => e.Key,
                e => new
                {
                    status = e.Value.Status.ToString().ToLowerInvariant(),
                    description = e.Value.Description,
                }),
        });
    },
});

app.MapGet("/", () => Results.Ok(new { service = "sso-api", version = "0.1.0" }));

app.Run();

public partial class Program;
