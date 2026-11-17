using System.Net;
using FluentAssertions;
using Sso.Application.Oidc;
using Sso.Domain.Entities;
using Sso.Domain.Enums;

namespace Sso.Application.Tests.Oidc;

/// <summary>FR-C01/C02 — client registry + secret shown once, stored hashed.</summary>
public class ClientsServiceTests
{
    private static readonly IPAddress Ip = IPAddress.Parse("10.0.0.1");

    [Fact]
    public async Task Create_confidential_returns_plaintext_once_and_stores_hash()
    {
        using var h = new OidcHarness();

        var result = await h.Clients.CreateAsync(
            new CreateClientRequest(null, "Agent Office", "confidential",
                ["https://office.example.com/cb"], null, null, null, null, null),
            Guid.NewGuid(), Ip, "ua", default);

        result.PlainSecret.Should().NotBeNullOrEmpty();          // shown once (FR-C02)
        var stored = h.Db.Clients.Single();
        stored.SecretHash.Should().NotBeNullOrEmpty()
            .And.NotBe(result.PlainSecret);                      // hash only at rest
        stored.ClientId.Should().StartWith("cli_");
        stored.RedirectUris.Should().Equal("https://office.example.com/cb");
    }

    [Fact]
    public async Task Create_public_client_has_no_secret()
    {
        using var h = new OidcHarness();

        var result = await h.Clients.CreateAsync(
            new CreateClientRequest("spa-app", "SPA", "public",
                ["https://app.example.com/cb"], null, null, null, null, null),
            Guid.NewGuid(), Ip, "ua", default);

        result.PlainSecret.Should().BeNull();
        h.Db.Clients.Single().SecretHash.Should().BeNull();
        result.Client.ClientId.Should().Be("spa-app");
    }

    [Fact]
    public async Task Create_duplicate_client_id_conflicts()
    {
        using var h = new OidcHarness();
        h.SeedClient();

        var act = () => h.Clients.CreateAsync(
            new CreateClientRequest("agent-office", "Dup", "confidential", ["https://x.co/cb"], null, null, null, null, null),
            Guid.NewGuid(), Ip, "ua", default);

        var err = await act.Should().ThrowAsync<Sso.Application.Errors.ApiError>();
        err.Which.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Rotate_secret_replaces_hash_and_returns_new_plaintext()
    {
        using var h = new OidcHarness();
        var client = h.SeedClient(secret: "old-secret");
        var oldHash = client.SecretHash;

        var result = await h.Clients.RotateSecretAsync(client.Id, Guid.NewGuid(), Ip, "ua", default);

        result.PlainSecret.Should().NotBeNullOrEmpty();
        h.Db.Clients.Single().SecretHash.Should().NotBe(oldHash);
    }

    [Fact]
    public async Task Update_changes_fields_and_audits()
    {
        using var h = new OidcHarness();
        var client = h.SeedClient();

        var dto = await h.Clients.UpdateAsync(client.Id,
            new UpdateClientRequest("Renamed", ["https://new.example.com/cb"], null, false, null, null, null, null),
            Guid.NewGuid(), Ip, "ua", default);

        dto.Name.Should().Be("Renamed");
        dto.SkipConsent.Should().BeFalse();
        dto.RedirectUris.Should().Equal("https://new.example.com/cb");
        h.Audit.Entries.Should().Contain(e => e.Event == AuditEvents.ClientUpdated);
    }

    [Fact]
    public async Task List_returns_registered_clients()
    {
        using var h = new OidcHarness();
        h.SeedClient();
        h.SeedClient(clientId: "other", secret: null, type: ClientType.Public);

        var list = await h.Clients.ListAsync(default);

        list.Should().HaveCount(2);
    }
}
