using System.Net;
using FluentAssertions;
using Sso.Application.Oidc;
using Sso.Domain.Enums;

namespace Sso.Application.Tests.Oidc;

/// <summary>KD-14 — per-user app assignments feeding the /dashboard launcher.</summary>
public class AssignmentsServiceTests
{
    private static readonly IPAddress Ip = IPAddress.Parse("10.0.0.1");

    private static AssignmentsService Service(OidcHarness h)
        => new(h.Db, h.Audit, h.Clock);

    [Fact]
    public async Task MyApps_returns_only_assigned_clients_with_launch_url()
    {
        using var h = new OidcHarness();
        var user = h.SeedUser();
        var assigned = h.SeedClient("app-a", launchUrl: "https://a.example.com");
        h.SeedClient("app-b", launchUrl: "https://b.example.com");          // not assigned
        var noLaunch = h.SeedClient("app-c");                              // assigned but no launch_url
        h.SeedAssignment(user, assigned);
        h.SeedAssignment(user, noLaunch);

        var apps = await Service(h).ListForUserAsync(user.Id, default);

        apps.Should().ContainSingle()
            .Which.ClientId.Should().Be("app-a");
    }

    [Fact]
    public async Task MyApps_admin_sees_all_launchable_clients_without_assignment()
    {
        using var h = new OidcHarness();
        var admin = h.SeedUser(admin: true);
        h.SeedClient("app-a", launchUrl: "https://a.example.com");
        h.SeedClient("app-b", launchUrl: "https://b.example.com");
        h.SeedClient("oidc-only");                                         // no launch_url
        h.SeedClient("off", launchUrl: "https://off.example.com", active: false);

        var apps = await Service(h).ListForUserAsync(admin.Id, default);

        apps.Select(a => a.ClientId).Should().Equal("app-a", "app-b");
    }

    [Fact]
    public async Task SetForUser_grants_and_revokes_with_audit()
    {
        using var h = new OidcHarness();
        var svc = Service(h);
        var user = h.SeedUser();
        var actor = h.SeedUser("admin@example.com", admin: true);
        var a = h.SeedClient("app-a", launchUrl: "https://a.example.com");
        var b = h.SeedClient("app-b", launchUrl: "https://b.example.com");
        var c = h.SeedClient("app-c", launchUrl: "https://c.example.com");
        await svc.SetForUserAsync(user.Id, new SetUserAppsRequest(["app-a", "app-b"]),
            actor.Id, Ip, "ua", default);

        var ids = await svc.SetForUserAsync(user.Id, new SetUserAppsRequest(["app-b", "app-c"]),
            actor.Id, Ip, "ua", default);

        ids.Should().Equal("app-b", "app-c");
        h.Db.ClientAssignments.Where(x => x.UserId == user.Id)
            .Select(x => x.ClientId).Should().BeEquivalentTo([b.Id, c.Id]);
        h.Audit.Entries.Should().Contain(e => e.Event == AuditEvents.AppGranted);
        h.Audit.Entries.Should().Contain(e => e.Event == AuditEvents.AppRevoked);
    }

    [Fact]
    public async Task SetForUser_is_idempotent_for_same_set()
    {
        using var h = new OidcHarness();
        var svc = Service(h);
        var user = h.SeedUser();
        h.SeedClient("app-a", launchUrl: "https://a.example.com");

        await svc.SetForUserAsync(user.Id, new SetUserAppsRequest(["app-a"]),
            Guid.NewGuid(), Ip, "ua", default);
        var granted = h.Audit.Entries.Count(e => e.Event == AuditEvents.AppGranted);
        await svc.SetForUserAsync(user.Id, new SetUserAppsRequest(["app-a"]),
            Guid.NewGuid(), Ip, "ua", default);

        h.Db.ClientAssignments.Count().Should().Be(1);
        h.Audit.Entries.Count(e => e.Event == AuditEvents.AppGranted).Should().Be(granted);
    }

    [Fact]
    public async Task SetForUser_unknown_client_id_is_validation_error()
    {
        using var h = new OidcHarness();
        var user = h.SeedUser();

        var act = () => Service(h).SetForUserAsync(user.Id,
            new SetUserAppsRequest(["ghost"]), Guid.NewGuid(), Ip, "ua", default);

        var err = await act.Should().ThrowAsync<Sso.Application.Errors.ApiError>();
        err.Which.StatusCode.Should().Be(400);
        h.Db.ClientAssignments.Should().BeEmpty();
    }

    [Fact]
    public async Task SetForUser_unknown_user_is_not_found()
    {
        using var h = new OidcHarness();

        var act = () => Service(h).SetForUserAsync(Guid.NewGuid(),
            new SetUserAppsRequest([]), Guid.NewGuid(), Ip, "ua", default);

        var err = await act.Should().ThrowAsync<Sso.Application.Errors.ApiError>();
        err.Which.StatusCode.Should().Be(404);
    }
}
