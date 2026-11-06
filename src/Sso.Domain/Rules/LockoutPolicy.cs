namespace Sso.Domain.Rules;

/// <summary>Progressive login lockout per FR-A02: 5 failures → 5 min, 10 → 30 min.</summary>
public static class LockoutPolicy
{
    public const int FirstThreshold = 5;
    public const int SecondThreshold = 10;
    public static readonly TimeSpan FirstLockout = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan SecondLockout = TimeSpan.FromMinutes(30);

    /// <summary>Lockout duration after N consecutive failures; null = not locked.</summary>
    public static TimeSpan? LockoutFor(int consecutiveFailures)
        => consecutiveFailures >= SecondThreshold ? SecondLockout
         : consecutiveFailures >= FirstThreshold ? FirstLockout
         : null;
}
