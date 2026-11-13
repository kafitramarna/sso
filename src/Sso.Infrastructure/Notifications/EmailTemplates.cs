using System.Net;

namespace Sso.Infrastructure.Notifications;

/// <summary>
/// Auth emails — HTML in the Observatory language (paper, ink, venus gold)
/// with a plain-text part alongside (multipart/alternative for deliverability).
/// Email-safe only: table layout, fully inline styles, no external assets.
/// </summary>
public static class EmailTemplates
{
    public sealed record Template(string Subject, string Text, string Html);

    public static Template VerifyEmail(string url) => new(
        "Verify your SSO email",
        $"""
        Welcome to VenusVerse SSO.

        Confirm this email address by opening the link below (valid 24 hours):

        {url}

        If you did not create this account, ignore this message.
        """,
        Layout(
            "Verify your email",
            "Confirm this address to finish setting up your VenusVerse account.",
            "Verify email",
            url,
            "This link expires in 24 hours.",
            "If you did not create this account, you can ignore this email."));

    public static Template PasswordReset(string url) => new(
        "Reset your SSO password",
        $"""
        A password reset was requested for this account.

        Set a new password via the link below (valid 1 hour, single use):

        {url}

        If you did not request this, ignore this message — your password is unchanged.
        """,
        Layout(
            "Reset your password",
            "A password reset was requested for your account.",
            "Reset password",
            url,
            "This link expires in 1 hour and can be used once.",
            "Did not request this? Your password is unchanged — you can ignore this email."));

    public static Template Invited(string url) => new(
        "You have been invited",
        $"""
        An administrator invited you to VenusVerse SSO.

        Set your password via the link below (valid 72 hours, single use):

        {url}

        If you were not expecting this, ignore this message.
        """,
        Layout(
            "You are invited",
            "An administrator invited you to VenusVerse SSO.",
            "Accept invite",
            url,
            "This invite expires in 72 hours and can be used once.",
            "If you were not expecting this, you can ignore this email."));

    private static string Layout(
        string title, string lede, string cta, string url, string expiry, string ignore)
    {
        var u = WebUtility.HtmlEncode(url);
        return $"""
            <!doctype html>
            <html lang="en">
            <body style="margin:0;padding:0;background:#f7f6f3;-webkit-font-smoothing:antialiased">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f7f6f3">
                <tr><td align="center" style="padding:40px 16px">
                  <table role="presentation" width="560" cellpadding="0" cellspacing="0" style="width:100%;max-width:560px">

                    <tr><td style="padding:0 4px 18px;font-family:-apple-system,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif">
                      <span style="display:inline-block;width:9px;height:9px;border-radius:50%;background:#9f7123;vertical-align:middle"></span><span style="display:inline-block;vertical-align:middle;padding-left:9px;font-size:16px;font-weight:600;color:#0b0e13;letter-spacing:-0.01em">VenusVerse</span><span style="display:inline-block;vertical-align:middle;padding-left:7px;font-size:13px;color:#83817a">SSO</span>
                    </td></tr>

                    <tr><td style="background:#ffffff;border:1px solid #e6e3da;border-radius:10px;padding:36px 38px">
                      <h1 style="margin:0 0 10px;font-family:-apple-system,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif;font-size:22px;line-height:1.3;font-weight:600;color:#0b0e13;letter-spacing:-0.01em">{title}</h1>
                      <p style="margin:0 0 26px;font-family:-apple-system,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif;font-size:14px;line-height:1.65;color:#3d3f45">{lede}</p>

                      <table role="presentation" cellpadding="0" cellspacing="0"><tr>
                        <td bgcolor="#0b0e13" style="border-radius:6px">
                          <a href="{u}" style="display:inline-block;padding:13px 30px;font-family:-apple-system,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif;font-size:14px;font-weight:600;color:#f7f6f3;text-decoration:none">{cta}</a>
                        </td>
                      </tr></table>

                      <p style="margin:22px 0 0;font-family:-apple-system,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif;font-size:12px;color:#83817a">{expiry}</p>

                      <hr style="border:none;border-top:1px solid #eceae2;margin:26px 0">
                      <p style="margin:0 0 6px;font-family:-apple-system,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif;font-size:12px;color:#83817a">Button not working? Paste this link into your browser:</p>
                      <p style="margin:0;font-family:SFMono-Regular,Consolas,Menlo,monospace;font-size:11px;line-height:1.6;color:#6b6a63;word-break:break-all">{u}</p>
                    </td></tr>

                    <tr><td style="padding:20px 6px;font-family:-apple-system,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif;font-size:11px;line-height:1.7;color:#a3a199">
                      {ignore}<br>
                      VenusVerse SSO — self-hosted identity for the VenusVerse ecosystem.
                    </td></tr>

                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;
    }
}
