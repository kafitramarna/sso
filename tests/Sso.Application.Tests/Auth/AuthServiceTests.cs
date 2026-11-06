using System.Net;
using FluentAssertions;
using Sso.Application.Auth;
using Sso.Application.Errors;
using Sso.Domain.Enums;

namespace Sso.Application.Tests.Auth;

public class AuthServiceTests
{
    private static readonly IPAddress Ip = IPAddress.Parse("10.0.0.1");

    // --- register (KD-09) ---

    [Fact]
    public async Task Register_when_self_registration_disabled_throws_forbidden()
    {
        using var h = new TestHarness(allowSelfRegistration: false);

        var act = () => h.Service.RegisterAsync(
            new RegisterRequest("a@b.co", "password-long-enough", null), Ip, "ua");

        var err = await act.Should().ThrowAsync<ApiError>();
        err.Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Register_creates_unverified_user_and_emails_verify_link()
    {
        using var h = new TestHarness();

        await h.Service.RegisterAsync(new RegisterRequest("New@Example.com", "password-long-enough", "New"), Ip, "ua");

        var user = h.Db.Users.Single();
        user.Email.Should().Be("new@example.com");          // normalized
        user.EmailVerified.Should().BeFalse();
        user.PasswordHash.Should().NotBeNull();
        h.Notifier.Sent.Should().ContainSingle(n => n.Kind == "verify" && n.Url.Contains("token="));
    }

    [Fact]
    public async Task Register_rejects_short_password()
    {
        using var h = new TestHarness();

        var act = () => h.Service.RegisterAsync(new RegisterRequest("a@b.co", "short", null), Ip, "ua");

        var err = await act.Should().ThrowAsync<ApiError>();
        err.Which.Code.Should().Be("validation_failed");
        h.Db.Users.Should().BeEmpty();
    }

    [Fact]
    public async Task Register_existing_email_succeeds_silently_without_duplicate()
    {
        using var h = new TestHarness();
        h.SeedUser("taken@example.com");

        // No enumeration (PRD §15): same outward result as a fresh register.
        await h.Service.RegisterAsync(new RegisterRequest("taken@example.com", "password-long-enough", null), Ip, "ua");

        h.Db.Users.Should().HaveCount(1);
    }

    // --- login (FR-A02) ---

    [Fact]
    public async Task Login_success_returns_tokens_resets_failures_and_audits()
    {
        using var h = new TestHarness();
        var user = h.SeedUser();
        user.FailedLoginCount = 2;
        h.Db.SaveChanges();

        var result = await h.Service.LoginAsync(new LoginRequest("user@example.com", "correct-password-1"), Ip, "ua");

        result.AccessToken.Should().Be("access-" + user.Id);
        result.RefreshToken.Should().StartWith("rt-");
        result.User.Email.Should().Be("user@example.com");
        user.FailedLoginCount.Should().Be(0);
        user.LastLoginAt.Should().Be(h.Clock.UtcNow);
        h.Audit.Entries.Should().Contain(e => e.Event == AuditEvents.AuthLogin && e.UserId == user.Id);
    }

    [Fact]
    public async Task Login_unverified_email_is_rejected_with_distinct_code()
    {
        using var h = new TestHarness();
        h.SeedUser(verified: false);

        var act = () => h.Service.LoginAsync(new LoginRequest("user@example.com", "correct-password-1"), Ip, "ua");

        var err = await act.Should().ThrowAsync<ApiError>();
        err.Which.Code.Should().Be("email_not_verified");
    }

    [Fact]
    public async Task Login_disabled_user_with_valid_password_is_rejected()
    {
        using var h = new TestHarness();
        h.SeedUser(disabled: true);

        var act = () => h.Service.LoginAsync(new LoginRequest("user@example.com", "correct-password-1"), Ip, "ua");

        var err = await act.Should().ThrowAsync<ApiError>();
        err.Which.Code.Should().Be("account_disabled");
    }

    [Fact]
    public async Task Login_wrong_password_is_generic_and_counted()
    {
        using var h = new TestHarness();
        var user = h.SeedUser();

        var act = () => h.Service.LoginAsync(new LoginRequest("user@example.com", "wrong-password-xx"), Ip, "ua");

        var err = await act.Should().ThrowAsync<ApiError>();
        err.Which.Message.Should().Be("Invalid email or password."); // identical for unknown user
        user.FailedLoginCount.Should().Be(1);
        h.Audit.Entries.Should().Contain(e => e.Event == AuditEvents.AuthLoginFailed);
    }

    [Fact]
    public async Task Login_fifth_failure_locks_five_minutes_then_valid_login_is_blocked()
    {
        using var h = new TestHarness();
        var user = h.SeedUser();

        for (var i = 0; i < 5; i++)
        {
            Func<Task> fail = () => h.Service.LoginAsync(new LoginRequest("user@example.com", "wrong"), Ip, "ua");
            await fail.Should().ThrowAsync<ApiError>();
        }

        user.LockedUntil.Should().Be(h.Clock.UtcNow + TimeSpan.FromMinutes(5));

        var act = () => h.Service.LoginAsync(new LoginRequest("user@example.com", "correct-password-1"), Ip, "ua");
        var err = await act.Should().ThrowAsync<ApiError>();
        err.Which.Message.Should().Contain("locked");
    }

    [Fact]
    public async Task Login_unknown_email_gives_identical_generic_error()
    {
        using var h = new TestHarness();

        var act = () => h.Service.LoginAsync(new LoginRequest("ghost@example.com", "whatever-password"), Ip, "ua");

        var err = await act.Should().ThrowAsync<ApiError>();
        err.Which.Message.Should().Be("Invalid email or password.");
    }

    // --- refresh rotation + reuse detection (KD-03) ---

    [Fact]
    public async Task Refresh_rotates_and_returns_new_pair()
    {
        using var h = new TestHarness();
        var user = h.SeedUser();
        var login = await h.Service.LoginAsync(new LoginRequest("user@example.com", "correct-password-1"), Ip, "ua");

        var result = await h.Service.RefreshAsync(login.RefreshToken, Ip, "ua");

        result.RefreshToken.Should().NotBe(login.RefreshToken);
        result.FamilyId.Should().Be(login.FamilyId);   // same session family
        h.Refresh.HasValidToken(login.RefreshToken).Should().BeFalse(); // old consumed
    }

    [Fact]
    public async Task Refresh_reused_rotated_token_revokes_whole_family()
    {
        using var h = new TestHarness();
        h.SeedUser();
        var login = await h.Service.LoginAsync(new LoginRequest("user@example.com", "correct-password-1"), Ip, "ua");
        var rotated = await h.Service.RefreshAsync(login.RefreshToken, Ip, "ua");

        // Attacker replays the original (stolen) token → family killed.
        var act = () => h.Service.RefreshAsync(login.RefreshToken, Ip, "ua");
        await act.Should().ThrowAsync<ApiError>();

        h.Refresh.RevokedFamilies.Should().Contain(login.FamilyId);
        // The legit rotated token is dead too — both sides must log in again.
        h.Refresh.HasValidToken(rotated.RefreshToken).Should().BeFalse();
        h.Audit.Entries.Should().Contain(e => e.Event == AuditEvents.AuthRefreshReuse);
    }

    [Fact]
    public async Task Logout_revokes_family()
    {
        using var h = new TestHarness();
        h.SeedUser();
        var login = await h.Service.LoginAsync(new LoginRequest("user@example.com", "correct-password-1"), Ip, "ua");

        await h.Service.LogoutAsync(login.RefreshToken, Ip, "ua");

        h.Refresh.HasValidToken(login.RefreshToken).Should().BeFalse();
        var act = () => h.Service.RefreshAsync(login.RefreshToken, Ip, "ua");
        await act.Should().ThrowAsync<ApiError>();
    }

    // --- email verify (FR-A01) ---

    [Fact]
    public async Task VerifyEmail_consumes_token_marks_verified_and_signs_in()
    {
        using var h = new TestHarness();
        await h.Service.RegisterAsync(new RegisterRequest("v@e.co", "password-long-enough", null), Ip, "ua");

        var result = await h.Service.VerifyEmailAsync(new VerifyEmailRequest(h.TokenFromLastUrl()), Ip, "ua");

        result.AccessToken.Should().StartWith("access-");
        h.Db.Users.Single().EmailVerified.Should().BeTrue();
    }

    [Fact]
    public async Task One_time_token_is_single_use()
    {
        using var h = new TestHarness();
        await h.Service.RegisterAsync(new RegisterRequest("v@e.co", "password-long-enough", null), Ip, "ua");
        var raw = h.TokenFromLastUrl();

        await h.Service.VerifyEmailAsync(new VerifyEmailRequest(raw), Ip, "ua");
        var act = () => h.Service.VerifyEmailAsync(new VerifyEmailRequest(raw), Ip, "ua");

        await act.Should().ThrowAsync<ApiError>();
    }

    // --- password reset (FR-A03) ---

    [Fact]
    public async Task ForgotPassword_unknown_email_succeeds_silently()
    {
        using var h = new TestHarness();

        await h.Service.ForgotPasswordAsync(new ForgotPasswordRequest("ghost@example.com"), Ip, "ua");

        h.Notifier.Sent.Should().BeEmpty();   // nothing leaked, nothing sent
    }

    [Fact]
    public async Task ResetPassword_sets_new_hash_and_revokes_all_sessions()
    {
        using var h = new TestHarness();
        var user = h.SeedUser();
        var login = await h.Service.LoginAsync(new LoginRequest("user@example.com", "correct-password-1"), Ip, "ua");
        await h.Service.ForgotPasswordAsync(new ForgotPasswordRequest("user@example.com"), Ip, "ua");
        var raw = h.TokenFromLastUrl();

        await h.Service.ResetPasswordAsync(new ResetPasswordRequest(raw, "brand-new-password"), Ip, "ua");

        h.Refresh.HasValidToken(login.RefreshToken).Should().BeFalse(); // sessions killed
        var relog = await h.Service.LoginAsync(new LoginRequest("user@example.com", "brand-new-password"), Ip, "ua");
        relog.AccessToken.Should().StartWith("access-");
    }

    // --- invite (FR-A04, KD-09) ---

    [Fact]
    public async Task CreateInvite_then_accept_produces_verified_signed_in_user()
    {
        using var h = new TestHarness(allowSelfRegistration: false);

        var invite = await h.Service.CreateInviteAsync(new CreateInviteRequest("invited@example.com", null), Guid.NewGuid(), Ip, "ua");
        var raw = h.TokenFromLastUrl();
        invite.InviteUrl.Should().Contain(raw);

        var result = await h.Service.AcceptInviteAsync(
            new AcceptInviteRequest(raw, "my-secure-password", "Invited"), Ip, "ua");

        result.AccessToken.Should().StartWith("access-");
        var user = h.Db.Users.Single(u => u.Email == "invited@example.com");
        user.EmailVerified.Should().BeTrue();
        user.DisplayName.Should().Be("Invited");
        h.Audit.Entries.Should().Contain(e => e.Event == AuditEvents.InviteAccepted);
    }

    // --- me / sessions (FR-A05, FR-D01) ---

    [Fact]
    public async Task ChangePassword_revokes_other_families_but_keeps_current()
    {
        using var h = new TestHarness();
        var user = h.SeedUser();
        var first = await h.Service.LoginAsync(new LoginRequest("user@example.com", "correct-password-1"), Ip, "ua-laptop");
        var second = await h.Service.RefreshAsync(first.RefreshToken, IPAddress.Parse("10.0.0.2"), "ua-phone");

        // `second` shares first's family — simulate a distinct third session.
        var other = await h.Service.LoginAsync(new LoginRequest("user@example.com", "correct-password-1"), Ip, "ua-other");

        await h.Service.ChangePasswordAsync(
            user.Id, first.FamilyId,
            new ChangePasswordRequest("correct-password-1", "new-password-here"), Ip, "ua");

        h.Refresh.HasValidToken(other.RefreshToken).Should().BeFalse(); // other family revoked
        h.Refresh.HasValidToken(second.RefreshToken).Should().BeTrue(); // current survives
    }

    [Fact]
    public async Task ListSessions_returns_families_and_marks_current()
    {
        using var h = new TestHarness();
        var user = h.SeedUser();
        var login = await h.Service.LoginAsync(new LoginRequest("user@example.com", "correct-password-1"), Ip, "ua");

        var sessions = await h.Service.ListSessionsAsync(user.Id, login.FamilyId);

        sessions.Should().ContainSingle(s => s.FamilyId == login.FamilyId && s.Current);
    }

    // --- admin (FR-D03 subset) ---

    [Fact]
    public async Task Disable_user_revokes_sessions_and_blocks_login()
    {
        using var h = new TestHarness();
        var user = h.SeedUser();
        var login = await h.Service.LoginAsync(new LoginRequest("user@example.com", "correct-password-1"), Ip, "ua");

        await h.Service.SetUserDisabledAsync(user.Id, disabled: true, Guid.NewGuid(), Ip, "ua");

        h.Refresh.HasValidToken(login.RefreshToken).Should().BeFalse();
        var act = () => h.Service.LoginAsync(new LoginRequest("user@example.com", "correct-password-1"), Ip, "ua");
        var err = await act.Should().ThrowAsync<ApiError>();
        err.Which.Code.Should().Be("account_disabled");
    }
}
