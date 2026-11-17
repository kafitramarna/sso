using System.Net;
using FluentAssertions;
using Sso.Application.Oidc;
using Sso.Domain.Enums;
using static Sso.Application.Oidc.AuthorizeOutcome;

namespace Sso.Application.Tests.Oidc;

/// <summary>FR-B02/B03 — authorize validation + token exchange with PKCE (KD-01).</summary>
public class OidcServiceTests
{
    private static readonly IPAddress Ip = IPAddress.Parse("10.0.0.1");
    private const string Redirect = "https://office.example.com/auth/callback";

    private static AuthorizeRequest ValidAuthorize(string? challenge = "chall", string scope = "openid profile")
        => new("code", "agent-office", Redirect, scope, "state-1", "nonce-1", challenge, "S256");

    // --- authorize validation ---

    [Fact]
    public async Task Authorize_unknown_client_is_invalid_request_not_redirect()
    {
        using var h = new OidcHarness();

        var outcome = await h.Service.DecideAuthorizeAsync(
            ValidAuthorize(), Guid.NewGuid(), "http://return", default);

        outcome.Should().BeOfType<InvalidRequest>();
    }

    [Fact]
    public async Task Authorize_unregistered_redirect_uri_never_redirects()
    {
        using var h = new OidcHarness();
        h.SeedClient();

        var outcome = await h.Service.DecideAuthorizeAsync(
            ValidAuthorize() with { RedirectUri = "https://evil.example.com/cb" },
            Guid.NewGuid(), "http://return", default);

        // PRD §15 / KD-01: unregistered redirect_uri → hard 400, never a redirect
        // to the attacker's URI.
        outcome.Should().BeOfType<InvalidRequest>();
    }

    [Fact]
    public async Task Authorize_missing_pkce_redirects_back_with_error()
    {
        using var h = new OidcHarness();
        h.SeedClient();
        var user = h.SeedUser();

        var outcome = await h.Service.DecideAuthorizeAsync(
            ValidAuthorize() with { CodeChallenge = null },
            user.Id, "http://return", default);

        var err = outcome.Should().BeOfType<RedirectError>().Subject;
        err.Uri.Should().StartWith(Redirect);
        err.Error.Should().Be("invalid_request");
        err.State.Should().Be("state-1");
    }

    [Fact]
    public async Task Authorize_wrong_challenge_method_redirects_with_error()
    {
        using var h = new OidcHarness();
        h.SeedClient();
        var user = h.SeedUser();

        var outcome = await h.Service.DecideAuthorizeAsync(
            ValidAuthorize() with { CodeChallengeMethod = "plain" },
            user.Id, "http://return", default);

        outcome.Should().BeOfType<RedirectError>();
    }

    [Fact]
    public async Task Authorize_without_openid_scope_redirects_with_error()
    {
        using var h = new OidcHarness();
        h.SeedClient();
        var user = h.SeedUser();

        var outcome = await h.Service.DecideAuthorizeAsync(
            ValidAuthorize(scope: "profile"),
            user.Id, "http://return", default);

        var err = outcome.Should().BeOfType<RedirectError>().Subject;
        err.Error.Should().Be("invalid_scope");
    }

    [Fact]
    public async Task Authorize_scope_beyond_registry_redirects_with_error()
    {
        using var h = new OidcHarness();
        h.SeedClient(scopes: ["openid"]);
        var user = h.SeedUser();

        var outcome = await h.Service.DecideAuthorizeAsync(
            ValidAuthorize(scope: "openid admin"),
            user.Id, "http://return", default);

        outcome.Should().BeOfType<RedirectError>()
            .Which.Error.Should().Be("invalid_scope");
    }

    [Fact]
    public async Task Authorize_no_session_redirects_to_login_preserving_return()
    {
        using var h = new OidcHarness();
        h.SeedClient();

        var outcome = await h.Service.DecideAuthorizeAsync(
            ValidAuthorize(), null, "http://localhost:5140/oauth/authorize?x=1", default);

        var login = outcome.Should().BeOfType<RedirectLogin>().Subject;
        login.Url.Should().StartWith("http://localhost:5175/login?return=");
        login.Url.Should().Contain("oauth%2Fauthorize");
    }

    [Fact]
    public async Task Authorize_happy_path_issues_code_and_audits()
    {
        using var h = new OidcHarness();
        var client = h.SeedClient();
        var user = h.SeedUser();

        var outcome = await h.Service.DecideAuthorizeAsync(
            ValidAuthorize(), user.Id, "http://return", default);

        var ok = outcome.Should().BeOfType<RedirectCode>().Subject;
        ok.Uri.Should().StartWith(Redirect + "?code=");
        ok.Uri.Should().Contain("state=state-1");
        ok.Code.Should().NotBeNullOrEmpty();
        h.Audit.Entries.Should().Contain(e => e.Event == AuditEvents.OidcAuthorize);
    }

    [Fact]
    public async Task Authorize_non_skip_consent_client_redirects_error_in_v1()
    {
        using var h = new OidcHarness();
        h.SeedClient(skipConsent: false);
        var user = h.SeedUser();

        var outcome = await h.Service.DecideAuthorizeAsync(
            ValidAuthorize(), user.Id, "http://return", default);

        // v1 only supports implicit consent (KD-04); explicit consent UI is later.
        outcome.Should().BeOfType<RedirectError>()
            .Which.Error.Should().Be("consent_required");
    }

    // --- require_assignment (KD-14) ---

    [Fact]
    public async Task Authorize_require_assignment_denies_unassigned_user()
    {
        using var h = new OidcHarness();
        h.SeedClient(requireAssignment: true);
        var user = h.SeedUser();

        var outcome = await h.Service.DecideAuthorizeAsync(
            ValidAuthorize(), user.Id, "http://return", default);

        var err = outcome.Should().BeOfType<RedirectError>().Subject;
        err.Error.Should().Be("access_denied");
        err.Uri.Should().StartWith(Redirect);
        err.State.Should().Be("state-1");
        h.Audit.Entries.Should().Contain(e => e.Event == AuditEvents.OidcAuthorizeDenied);
    }

    [Fact]
    public async Task Authorize_require_assignment_allows_assigned_user()
    {
        using var h = new OidcHarness();
        var client = h.SeedClient(requireAssignment: true);
        var user = h.SeedUser();
        h.SeedAssignment(user, client);

        var outcome = await h.Service.DecideAuthorizeAsync(
            ValidAuthorize(), user.Id, "http://return", default);

        outcome.Should().BeOfType<RedirectCode>();
    }

    [Fact]
    public async Task Authorize_require_assignment_allows_sso_admin_without_assignment()
    {
        using var h = new OidcHarness();
        h.SeedClient(requireAssignment: true);
        var admin = h.SeedUser(admin: true);

        var outcome = await h.Service.DecideAuthorizeAsync(
            ValidAuthorize(), admin.Id, "http://return", default);

        outcome.Should().BeOfType<RedirectCode>();
    }

    // --- token exchange (authorization_code grant) ---

    private async Task<(string Code, string Verifier)> IssueCodeAsync(
        OidcHarness h, Guid userId, string scope = "openid profile offline_access")
    {
        var (verifier, challenge) = OidcHarness.Pkce();
        var outcome = await h.Service.DecideAuthorizeAsync(
            ValidAuthorize(challenge, scope), userId, "http://return", default);
        outcome.Should().BeOfType<RedirectCode>();
        return (((RedirectCode)outcome).Code, verifier);
    }

    [Fact]
    public async Task Token_exchange_happy_path_mints_access_id_and_refresh()
    {
        using var h = new OidcHarness();
        h.SeedClient();
        var user = h.SeedUser();
        var (code, verifier) = await IssueCodeAsync(h, user.Id);

        var res = await h.Service.ExchangeTokenAsync(
            new TokenRequest("authorization_code", code, Redirect,
                "agent-office", "client-secret-1", verifier, null),
            Ip, "ua", default);

        res.TokenType.Should().Be("Bearer");
        res.AccessToken.Should().StartWith("oidc-access-agent-office");
        res.IdToken.Should().StartWith("id-token-agent-office");
        res.RefreshToken.Should().StartWith("ort-");       // offline_access granted
        res.Scope.Should().Be("openid profile offline_access");
        h.Tokens.LastOidcAccessArgs.Should().Be(("agent-office", "openid profile offline_access"));
        h.Tokens.LastIdTokenArgs!.Value.Nonce.Should().Be("nonce-1");
        h.Audit.Entries.Should().Contain(e => e.Event == AuditEvents.OidcToken);
    }

    [Fact]
    public async Task Token_exchange_without_offline_access_gets_no_refresh_token()
    {
        using var h = new OidcHarness();
        h.SeedClient();
        var user = h.SeedUser();
        var (code, verifier) = await IssueCodeAsync(h, user.Id, scope: "openid");

        var res = await h.Service.ExchangeTokenAsync(
            new TokenRequest("authorization_code", code, Redirect,
                "agent-office", "client-secret-1", verifier, null),
            Ip, "ua", default);

        res.RefreshToken.Should().BeNull();
    }

    [Fact]
    public async Task Token_exchange_replayed_code_fails_single_use()
    {
        using var h = new OidcHarness();
        h.SeedClient();
        var user = h.SeedUser();
        var (code, verifier) = await IssueCodeAsync(h, user.Id);

        await h.Service.ExchangeTokenAsync(
            new TokenRequest("authorization_code", code, Redirect,
                "agent-office", "client-secret-1", verifier, null), Ip, "ua", default);

        var act = () => h.Service.ExchangeTokenAsync(
            new TokenRequest("authorization_code", code, Redirect,
                "agent-office", "client-secret-1", verifier, null), Ip, "ua", default);
        var err = await act.Should().ThrowAsync<OidcError>();
        err.Which.Error.Should().Be("invalid_grant");
    }

    [Fact]
    public async Task Token_exchange_wrong_verifier_fails_pkce()
    {
        using var h = new OidcHarness();
        h.SeedClient();
        var user = h.SeedUser();
        var (code, _) = await IssueCodeAsync(h, user.Id);

        var act = () => h.Service.ExchangeTokenAsync(
            new TokenRequest("authorization_code", code, Redirect,
                "agent-office", "client-secret-1", "wrong-verifier", null), Ip, "ua", default);
        var err = await act.Should().ThrowAsync<OidcError>();
        err.Which.Error.Should().Be("invalid_grant");
    }

    [Fact]
    public async Task Token_exchange_wrong_secret_fails_invalid_client()
    {
        using var h = new OidcHarness();
        h.SeedClient();
        var user = h.SeedUser();
        var (code, verifier) = await IssueCodeAsync(h, user.Id);

        var act = () => h.Service.ExchangeTokenAsync(
            new TokenRequest("authorization_code", code, Redirect,
                "agent-office", "bad-secret", verifier, null), Ip, "ua", default);
        var err = await act.Should().ThrowAsync<OidcError>();
        err.Which.Error.Should().Be("invalid_client");
    }

    [Fact]
    public async Task Token_exchange_redirect_uri_mismatch_fails()
    {
        using var h = new OidcHarness();
        h.SeedClient();
        var user = h.SeedUser();
        var (code, verifier) = await IssueCodeAsync(h, user.Id);

        var act = () => h.Service.ExchangeTokenAsync(
            new TokenRequest("authorization_code", code, "https://other.example.com/cb",
                "agent-office", "client-secret-1", verifier, null), Ip, "ua", default);
        await act.Should().ThrowAsync<OidcError>();
    }

    // --- refresh_token grant ---

    [Fact]
    public async Task Refresh_grant_rotates_and_reissues_tokens()
    {
        using var h = new OidcHarness();
        h.SeedClient();
        var user = h.SeedUser();
        var (code, verifier) = await IssueCodeAsync(h, user.Id);
        var first = await h.Service.ExchangeTokenAsync(
            new TokenRequest("authorization_code", code, Redirect,
                "agent-office", "client-secret-1", verifier, null), Ip, "ua", default);

        var res = await h.Service.ExchangeTokenAsync(
            new TokenRequest("refresh_token", null, null,
                "agent-office", "client-secret-1", null, first.RefreshToken), Ip, "ua", default);

        res.RefreshToken.Should().NotBe(first.RefreshToken);
        res.AccessToken.Should().StartWith("oidc-access-agent-office");
    }

    [Fact]
    public async Task Refresh_token_cannot_be_used_by_another_client()
    {
        using var h = new OidcHarness();
        h.SeedClient();
        h.SeedClient(clientId: "other-app", secret: "other-secret");
        var user = h.SeedUser();
        var (code, verifier) = await IssueCodeAsync(h, user.Id);
        var first = await h.Service.ExchangeTokenAsync(
            new TokenRequest("authorization_code", code, Redirect,
                "agent-office", "client-secret-1", verifier, null), Ip, "ua", default);

        var act = () => h.Service.ExchangeTokenAsync(
            new TokenRequest("refresh_token", null, null,
                "other-app", "other-secret", null, first.RefreshToken), Ip, "ua", default);
        var err = await act.Should().ThrowAsync<OidcError>();
        err.Which.Error.Should().Be("invalid_grant");
    }

    [Fact]
    public async Task Revoke_kills_refresh_token()
    {
        using var h = new OidcHarness();
        h.SeedClient();
        var user = h.SeedUser();
        var (code, verifier) = await IssueCodeAsync(h, user.Id);
        var first = await h.Service.ExchangeTokenAsync(
            new TokenRequest("authorization_code", code, Redirect,
                "agent-office", "client-secret-1", verifier, null), Ip, "ua", default);

        await h.Service.RevokeAsync(
            new TokenRequest(null, null, null, "agent-office", "client-secret-1",
                null, first.RefreshToken), Ip, "ua", default);

        var act = () => h.Service.ExchangeTokenAsync(
            new TokenRequest("refresh_token", null, null,
                "agent-office", "client-secret-1", null, first.RefreshToken), Ip, "ua", default);
        await act.Should().ThrowAsync<OidcError>();
    }
}
