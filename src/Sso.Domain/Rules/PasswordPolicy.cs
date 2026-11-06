namespace Sso.Domain.Rules;

/// <summary>Password rules per FR-A01 (docs/01-PRD §6).</summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 12;

    /// <summary>Returns validation error codes; empty means the password is acceptable.</summary>
    public static IReadOnlyList<string> Validate(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return ["password_required"];
        if (password.Length < MinimumLength)
            return ["password_too_short"];
        return [];
    }
}
