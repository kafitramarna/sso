using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Sso.Api.Tests.Health;

/// <summary>FR-E01: /health/live = process up; /health/ready = dependency report (PRD §10).</summary>
public class HealthEndpointsTests : IClassFixture<SsoApiFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointsTests(SsoApiFactory factory)
        => _client = factory.CreateClient();

    [Fact]
    public async Task Live_returns_200_without_touching_dependencies()
    {
        var response = await _client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_reports_postgres_and_redis_status()
    {
        var response = await _client.GetAsync("/health/ready");

        // Without dev infra running the endpoint degrades to 503, but the
        // contract — per-dependency status entries — must always hold.
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().BeOneOf("healthy", "unhealthy", "degraded");

        var deps = body.GetProperty("dependencies");
        deps.GetProperty("postgres").GetProperty("status").GetString()
            .Should().BeOneOf("healthy", "unhealthy", "degraded");
        deps.GetProperty("redis").GetProperty("status").GetString()
            .Should().BeOneOf("healthy", "unhealthy", "degraded");
    }

    [Fact]
    public async Task Root_returns_service_descriptor()
    {
        var response = await _client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("service").GetString().Should().Be("sso-api");
    }
}
