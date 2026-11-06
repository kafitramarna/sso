using FluentAssertions;
using Sso.Domain.Rules;

namespace Sso.Domain.Tests.Rules;

/// <summary>FR-A02: progressive lockout — 5 failures → 5 min, 10 → 30 min (PRD §6).</summary>
public class LockoutPolicyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void Below_first_threshold_returns_no_lockout(int failures)
        => LockoutPolicy.LockoutFor(failures).Should().BeNull();

    [Theory]
    [InlineData(5)]
    [InlineData(9)]
    public void First_threshold_locks_for_five_minutes(int failures)
        => LockoutPolicy.LockoutFor(failures).Should().Be(TimeSpan.FromMinutes(5));

    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    public void Second_threshold_locks_for_thirty_minutes(int failures)
        => LockoutPolicy.LockoutFor(failures).Should().Be(TimeSpan.FromMinutes(30));
}
