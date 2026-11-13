using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Sso.Api.Tests.Auth;

/// <summary>End-to-end session flow against the seeded factory (FR-A, KD-03/09).</summary>
public class AuthEndpointsTests : IClassFixture<SsoApiFactory>
{
    private readonly HttpClient _client;

    public AuthEndpointsTests(SsoApiFactory factory)
        => _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
        });

    [Fact]
    public async Task Seeded_admin_logs_in_and_reads_me()
    {
        var login = await _client.PostAsJsonAsync("/v1/auth/login",
            new { email = "admin@test.local", password = "admin-test-password" });

        login.StatusCode.Should().Be(HttpStatusCode.OK);
        login.Headers.Should().Contain(h =>
            h.Key == "Set-Cookie" && h.Value!.Any(v => v.Contains("sso_rt")));

        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("data").GetProperty("token").GetString()!;

        using var me = new HttpRequestMessage(HttpMethod.Get, "/v1/me");
        me.Headers.Authorization = new("Bearer", token);
        var meRes = await _client.SendAsync(me);

        meRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var meBody = await meRes.Content.ReadFromJsonAsync<JsonElement>();
        meBody.GetProperty("data").GetProperty("user")
            .GetProperty("email").GetString().Should().Be("admin@test.local");
    }

    [Fact]
    public async Task Login_wrong_password_returns_generic_401_envelope()
    {
        var res = await _client.PostAsJsonAsync("/v1/auth/login",
            new { email = "admin@test.local", password = "wrong-password" });

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("message").GetString()
            .Should().Be("Invalid email or password.");
    }

    [Fact]
    public async Task Register_returns_403_when_self_registration_disabled()
    {
        var res = await _client.PostAsJsonAsync("/v1/auth/register",
            new { email = "new@test.local", password = "password-long-enough" });

        res.StatusCode.Should().Be(HttpStatusCode.Forbidden); // KD-09
    }

    [Fact]
    public async Task Invite_accept_login_refresh_logout_full_cycle()
    {
        // 1. admin creates invite via X-Admin-Key (KD-06)
        using var inviteReq = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/invites");
        inviteReq.Headers.Add("X-Admin-Key", "test-admin-key");
        inviteReq.Content = JsonContent.Create(new { email = "invited@test.local" });
        var invite = await _client.SendAsync(inviteReq);

        invite.StatusCode.Should().Be(HttpStatusCode.Created);
        var inviteBody = await invite.Content.ReadFromJsonAsync<JsonElement>();
        var url = inviteBody.GetProperty("data").GetProperty("invite_url").GetString()!;
        var token = url.Split("token=")[1];

        // 2. invitee accepts → signed in
        var accept = await _client.PostAsJsonAsync("/v1/auth/accept-invite",
            new { token, password = "invited-password-1", display_name = "Invited" });
        accept.StatusCode.Should().Be(HttpStatusCode.OK);
        var acceptBody = await accept.Content.ReadFromJsonAsync<JsonElement>();
        var access = acceptBody.GetProperty("data").GetProperty("token").GetString()!;

        // 3. session endpoints with Bearer token
        using var sessionsReq = new HttpRequestMessage(HttpMethod.Get, "/v1/sessions");
        sessionsReq.Headers.Authorization = new("Bearer", access);
        var sessions = await _client.SendAsync(sessionsReq);
        sessions.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. refresh rotates the cookie (cookie jar is handled by the client)
        var refresh = await _client.PostAsync("/v1/auth/refresh", null);
        refresh.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. logout kills the family
        var logout = await _client.PostAsync("/v1/auth/logout", null);
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refreshAfter = await _client.PostAsync("/v1/auth/refresh", null);
        refreshAfter.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_endpoint_rejects_missing_key()
    {
        var res = await _client.PostAsJsonAsync("/v1/admin/invites",
            new { email = "x@test.local" });

        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Me_without_token_returns_401_envelope()
    {
        var res = await _client.GetAsync("/v1/me");

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("success").GetBoolean().Should().BeFalse();
    }
}
