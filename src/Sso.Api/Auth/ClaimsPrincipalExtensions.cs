using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Sso.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    public static Guid? UserId(this ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirstValue("sub"), out var id) ? id : null;

    public static Guid? SessionFamily(this ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirstValue("fam"), out var id) ? id : null;

    /// <summary>KD-06 admin guard: sso_admin role claim OR matching X-Admin-Key.</summary>
    public static bool IsAdminRequest(this HttpContext http, IConfiguration config)
    {
        if (http.User.IsInRole("sso_admin"))
            return true;
        var configured = config["Admin:ApiKey"];
        return !string.IsNullOrEmpty(configured)
            && http.Request.Headers.TryGetValue("X-Admin-Key", out var provided)
            && FixedTimeEquals(configured, provided!);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}
