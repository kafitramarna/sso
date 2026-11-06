namespace Sso.Infrastructure.Notifications;

/// <summary>Plain-text auth emails — same spirit as plotsure's templates.</summary>
public static class EmailTemplates
{
    public sealed record Template(string Subject, string Body);

    public static Template VerifyEmail(string url) => new(
        "Verify your SSO email",
        $"""
        Welcome to VenusVerse SSO.

        Confirm this email address by opening the link below (valid 24 hours):

        {url}

        If you did not create this account, ignore this message.
        """);

    public static Template PasswordReset(string url) => new(
        "Reset your SSO password",
        $"""
        A password reset was requested for this account.

        Set a new password via the link below (valid 1 hour, single use):

        {url}

        If you did not request this, ignore this message — your password is unchanged.
        """);

    public static Template Invited(string url) => new(
        "You have been invited",
        $"""
        An administrator invited you to VenusVerse SSO.

        Set your password via the link below (valid 72 hours, single use):

        {url}

        If you were not expecting this, ignore this message.
        """);
}
