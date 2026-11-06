using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Sso.Api.Tests.Oidc;

/// <summary>
/// FR-B end-to-end: discovery/JWKS contract + the complete code+PKCE round
/// trip against the seeded agent-office client (KD-01/KD-08).
/// </summary>
public class OidcFlowTests : IClassFixture<SsoApiFactory>
{
    private readonly SsoApiFactory _factory;

    public OidcFlowTests(SsoApiFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = true,
        AllowAutoRedirect = false, // 302s carry the authorization code — inspect, don't follow
    });

    private static (string Verifier, string Challenge) Pkce()
    {
        var verifier = Convert.ToBase64String(Guid.NewGuid().ToByteArray()
            .Concat(Guid.NewGuid().ToByteArray()).Concat(Guid.NewGuid().ToByteArray()).ToArray())
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var challenge = Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return (verifier, challenge);
    }

    private static string AuthorizeUrl(string challenge, string scope = "openid profile email offline_access")
        => "/oauth/authorize?" + string.Join('&', new[]
        {
            "response_type=code",
            "client_id=agent-office",
            $"redirect_uri={Uri.EscapeDataString("https://office.test/callback")}",
            $"scope={Uri.EscapeDataString(scope)}",
            "state=st-1", "nonce=n-1",
            $"code_challenge={challenge}",
            "code_challenge_method=S256",
        });

    private static async Task LoginAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync("/v1/auth/login",
            new { email = "admin@test.local", password = "admin-test-password" });
        login.EnsureSuccessStatusCode();
    }

    // --- discovery + jwks contract ---

    [Fact]
    public async Task Discovery_document_matches_registered_endpoints()
    {
        using var client = NewClient();

        var res = await client.GetAsync("/.well-known/openid-configuration");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await res.Content.ReadFromJsonAsync<JsonElement>();
        doc.GetProperty("issuer").GetString().Should().Be("sso-test");
        doc.GetProperty("authorization_endpoint").GetString().Should().Be("sso-test/oauth/authorize");
        doc.GetProperty("token_endpoint").GetString().Should().Be("sso-test/oauth/token");
        doc.GetProperty("jwks_uri").GetString().Should().Be("sso-test/.well-known/jwks.json");
        doc.GetProperty("code_challenge_methods_supported").EnumerateArray()
            .Select(e => e.GetString()).Should().Equal("S256");
        doc.GetProperty("response_types_supported").EnumerateArray()
            .Select(e => e.GetString()).Should().Equal("code");
    }

    [Fact]
    public async Task Jwks_publishes_rs256_public_key()
    {
        using var client = NewClient();

        var res = await client.GetAsync("/.well-known/jwks.json");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await res.Content.ReadFromJsonAsync<JsonElement>();
        var key = doc.GetProperty("keys").EnumerateArray().Single();
        key.GetProperty("kty").GetString().Should().Be("RSA");
        key.GetProperty("alg").GetString().Should().Be("RS256");
        key.GetProperty("kid").GetString().Should().NotBeNullOrEmpty();
        key.GetProperty("n").GetString().Should().NotBeNullOrEmpty();
        key.GetProperty("e").GetString().Should().Be("AQAB");
    }

    // --- authorize negatives ---

    [Fact]
    public async Task Authorize_unknown_client_returns_400_never_redirects()
    {
        using var client = NewClient();

        var res = await client.GetAsync("/oauth/authorize?response_type=code" +
            "&client_id=nobody&redirect_uri=https://x.test/cb&scope=openid");

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("invalid_client");
    }

    [Fact]
    public async Task Authorize_unregistered_redirect_uri_returns_400()
    {
        using var client = NewClient();

        var res = await client.GetAsync(AuthorizeUrl("c1")
            .Replace(Uri.EscapeDataString("https://office.test/callback"),
                Uri.EscapeDataString("https://evil.test/cb")));

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Authorize_without_session_redirects_to_spa_login()
    {
        using var client = NewClient();
        var (verifier, challenge) = Pkce();

        var res = await client.GetAsync(AuthorizeUrl(challenge));

        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = res.Headers.Location!.ToString();
        location.Should().Contain("/login?return=");
        location.Should().Contain("oauth%2Fauthorize");
    }

    // --- the full round trip ---

    [Fact]
    public async Task Full_code_pkce_flow_exchange_userinfo_refresh_revoke()
    {
        using var client = NewClient();
        await LoginAsync(client);
        var (verifier, challenge) = Pkce();

        // 1. authorize → 302 back to the client's redirect_uri with ?code&state
        var authz = await client.GetAsync(AuthorizeUrl(challenge));
        authz.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = authz.Headers.Location!.ToString();
        location.Should().StartWith("https://office.test/callback?code=");
        location.Should().Contain("state=st-1");
        var code = location.Split("code=")[1].Split('&')[0];

        // 2. token exchange (Basic client auth) → access + id + refresh
        using var tokenReq = new HttpRequestMessage(HttpMethod.Post, "/oauth/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = "https://office.test/callback",
                ["code_verifier"] = verifier,
            }),
        };
        tokenReq.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("agent-office:test-agent-secret")));
        var tokenRes = await client.SendAsync(tokenReq);

        tokenRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokens = await tokenRes.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = tokens.GetProperty("access_token").GetString()!;
        tokens.GetProperty("token_type").GetString().Should().Be("Bearer");
        tokens.GetProperty("id_token").GetString().Should().NotBeNullOrEmpty();
        var refreshToken = tokens.GetProperty("refresh_token").GetString()!;
        tokens.GetProperty("scope").GetString().Should().Be("openid profile email offline_access");

        // access_token is a real RS256 JWT
        accessToken.Split('.').Should().HaveCount(3);

        // 3. userinfo with the access token → sub + scoped claims
        using var infoReq = new HttpRequestMessage(HttpMethod.Get, "/oauth/userinfo");
        infoReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var info = await client.SendAsync(infoReq);

        info.StatusCode.Should().Be(HttpStatusCode.OK);
        var claims = await info.Content.ReadFromJsonAsync<JsonElement>();
        claims.GetProperty("sub").GetString().Should().NotBeNullOrEmpty();
        claims.GetProperty("email").GetString().Should().Be("admin@test.local");

        // 4. replay the code → invalid_grant (single use)
        using var replayReq = new HttpRequestMessage(HttpMethod.Post, "/oauth/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = "https://office.test/callback",
                ["code_verifier"] = verifier,
            }),
        };
        replayReq.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("agent-office:test-agent-secret")));
        var replay = await client.SendAsync(replayReq);
        replay.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await replay.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("error").GetString().Should().Be("invalid_grant");

        // 5. refresh grant rotates the refresh token
        var refresh = await client.PostAsync("/oauth/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = "agent-office",
                ["client_secret"] = "test-agent-secret",
            }));
        refresh.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshed = await refresh.Content.ReadFromJsonAsync<JsonElement>();
        var newRefresh = refreshed.GetProperty("refresh_token").GetString()!;
        newRefresh.Should().NotBe(refreshToken);

        // 6. revoke → subsequent refresh fails
        var revoke = await client.PostAsync("/oauth/revoke",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = newRefresh,
                ["client_id"] = "agent-office",
                ["client_secret"] = "test-agent-secret",
            }));
        revoke.StatusCode.Should().Be(HttpStatusCode.OK);

        var postRevoke = await client.PostAsync("/oauth/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = newRefresh,
                ["client_id"] = "agent-office",
                ["client_secret"] = "test-agent-secret",
            }));
        postRevoke.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Token_exchange_with_wrong_verifier_is_invalid_grant()
    {
        using var client = NewClient();
        await LoginAsync(client);
        var (_, challenge) = Pkce();

        var authz = await client.GetAsync(AuthorizeUrl(challenge));
        var code = authz.Headers.Location!.ToString().Split("code=")[1].Split('&')[0];

        var res = await client.PostAsync("/oauth/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = "https://office.test/callback",
                ["code_verifier"] = new string('x', 64),
                ["client_id"] = "agent-office",
                ["client_secret"] = "test-agent-secret",
            }));

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await res.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    [Fact]
    public async Task Token_exchange_with_bad_secret_is_401()
    {
        using var client = NewClient();
        await LoginAsync(client);
        var (verifier, challenge) = Pkce();

        var authz = await client.GetAsync(AuthorizeUrl(challenge));
        var code = authz.Headers.Location!.ToString().Split("code=")[1].Split('&')[0];

        var res = await client.PostAsync("/oauth/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = "https://office.test/callback",
                ["code_verifier"] = verifier,
                ["client_id"] = "agent-office",
                ["client_secret"] = "wrong-secret",
            }));

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await res.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("error").GetString().Should().Be("invalid_client");
    }

    // --- admin client registry through the API ---

    [Fact]
    public async Task Admin_can_create_client_and_secret_is_shown_once()
    {
        using var client = NewClient();

        using var createReq = new HttpRequestMessage(HttpMethod.Post, "/admin/clients")
        {
            Content = JsonContent.Create(new
            {
                name = "Test App",
                type = "confidential",
                redirect_uris = new[] { "https://app.test/cb" },
            }),
        };
        createReq.Headers.Add("X-Admin-Key", "test-admin-key");
        var created = await client.SendAsync(createReq);

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");
        data.GetProperty("client_secret").GetString().Should().NotBeNullOrEmpty();
        data.GetProperty("client").GetProperty("client_id").GetString()
            .Should().StartWith("cli_");

        // list must NOT leak the secret
        using var listReq = new HttpRequestMessage(HttpMethod.Get, "/admin/clients");
        listReq.Headers.Add("X-Admin-Key", "test-admin-key");
        var list = await client.SendAsync(listReq);
        (await list.Content.ReadAsStringAsync()).Should().NotContain("client_secret");
    }

    [Fact]
    public async Task Admin_endpoints_require_admin_key()
    {
        using var client = NewClient();

        var res = await client.GetAsync("/admin/clients");

        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
