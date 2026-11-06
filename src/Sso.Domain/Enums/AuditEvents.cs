namespace Sso.Domain.Enums;

/// <summary>audit_events.event values — append-only (FR-D02).</summary>
public static class AuditEvents
{
    public const string AuthRegistered = "auth.registered";
    public const string AuthLogin = "auth.login";
    public const string AuthLoginFailed = "auth.login_failed";
    public const string AuthLogout = "auth.logout";
    public const string AuthRefresh = "auth.refresh";
    public const string AuthRefreshReuse = "auth.refresh_reuse_detected";
    public const string EmailVerified = "email.verified";
    public const string PasswordResetRequested = "password.reset_requested";
    public const string PasswordReset = "password.reset";
    public const string PasswordChanged = "password.changed";
    public const string InviteCreated = "invite.created";
    public const string InviteAccepted = "invite.accepted";
    public const string SessionRevoked = "session.revoked";
    public const string SessionsRevokedAll = "session.revoked_all";
    public const string UserDisabled = "user.disabled";
    public const string UserEnabled = "user.enabled";

    public const string OidcAuthorize = "oidc.authorize";
    public const string OidcToken = "oidc.token";
    public const string OidcTokenFailed = "oidc.token_failed";
    public const string OidcRefreshReuse = "oidc.refresh_reuse_detected";
    public const string OidcRevoke = "oidc.revoke";
    public const string ClientCreated = "client.created";
    public const string ClientUpdated = "client.updated";
    public const string ClientSecretRotated = "client.secret_rotated";
}
