using FluentAssertions;
using Sso.Application.Errors;

namespace Sso.Application.Tests.Errors;

/// <summary>ApiError factories map to stable HTTP codes (PRD §10 envelope).</summary>
public class ApiErrorTests
{
    [Fact]
    public void Unauthenticated_maps_to_401()
    {
        var error = ApiError.Unauthenticated();

        error.StatusCode.Should().Be(401);
        error.Code.Should().Be("unauthenticated");
    }

    [Fact]
    public void Forbidden_maps_to_403()
    {
        var error = ApiError.Forbidden();

        error.StatusCode.Should().Be(403);
        error.Code.Should().Be("forbidden");
    }

    [Fact]
    public void NotFound_maps_to_404()
    {
        var error = ApiError.NotFound();

        error.StatusCode.Should().Be(404);
        error.Code.Should().Be("not_found");
    }

    [Fact]
    public void Validation_carries_details()
    {
        var details = new { field = "email" };
        var error = ApiError.Validation("Email is required.", details);

        error.StatusCode.Should().Be(400);
        error.Code.Should().Be("validation_failed");
        error.Details.Should().BeSameAs(details);
    }
}
