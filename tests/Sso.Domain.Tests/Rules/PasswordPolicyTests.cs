using FluentAssertions;
using Sso.Domain.Rules;

namespace Sso.Domain.Tests.Rules;

/// <summary>FR-A01: password minimum 12 characters.</summary>
public class PasswordPolicyTests
{
    [Theory]
    [InlineData("correct horse battery")]
    [InlineData("123456789012")]
    [InlineData("a]9$kM2!vXzQ")]
    public void Valid_passwords_pass(string password)
        => PasswordPolicy.Validate(password).Should().BeEmpty();

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("12345678901")]      // 11 chars — one under minimum
    [InlineData("           ")]      // whitespace-only
    public void Invalid_passwords_fail(string password)
        => PasswordPolicy.Validate(password).Should().NotBeEmpty();

    [Fact]
    public void Minimum_length_is_12()
        => PasswordPolicy.MinimumLength.Should().Be(12);
}
