using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;

namespace Sso.Api.Tests;

/// <summary>Layer rules per AGENTS.md: Api → Application → Domain; Infrastructure → Application.</summary>
public class ArchitectureTests
{
    private static readonly Assembly Domain = Assembly.Load("Sso.Domain");
    private static readonly Assembly Application = Assembly.Load("Sso.Application");
    private static readonly Assembly Infrastructure = Assembly.Load("Sso.Infrastructure");

    [Fact]
    public void Domain_does_not_depend_on_any_other_layer()
    {
        var result = Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny("Sso.Application", "Sso.Infrastructure", "Sso.Api")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Domain must stay reference-free. Violators: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_api()
    {
        var result = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOnAny("Sso.Infrastructure", "Sso.Api")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Application may only depend on Domain. Violators: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_api()
    {
        var result = Types.InAssembly(Infrastructure)
            .ShouldNot()
            .HaveDependencyOn("Sso.Api")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Infrastructure must not reach into the API layer. Violators: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
