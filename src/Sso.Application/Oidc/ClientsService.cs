using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Sso.Application.Abstractions;
using Sso.Application.Errors;
using Sso.Domain.Entities;
using Sso.Domain.Enums;

namespace Sso.Application.Oidc;

/// <summary>
/// Client registry (FR-C01): secrets are generated server-side, stored as
/// SHA-256, and the plaintext is returned exactly once (FR-C02).
/// </summary>
public sealed class ClientsService(ISsoDbContext db, IAuditWriter audit, IClock clock)
{
    private static readonly string[] DefaultScopes = ["openid", "profile", "email"];

    public async Task<IReadOnlyList<ClientDto>> ListAsync(CancellationToken ct = default)
        => (await db.Clients.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct))
            .Select(ToDto).ToList();

    public async Task<CreatedClientResult> CreateAsync(
        CreateClientRequest req, Guid actorId, IPAddress? ip, string? userAgent,
        CancellationToken ct = default)
    {
        var type = ParseType(req.Type);
        if (req.RedirectUris.Length == 0)
            throw ApiError.Validation("At least one redirect_uri is required.");
        if (string.IsNullOrWhiteSpace(req.Name))
            throw ApiError.Validation("name is required.");

        var clientId = string.IsNullOrWhiteSpace(req.ClientId)
            ? $"cli_{Base64Url(RandomNumberGenerator.GetBytes(9))}"
            : req.ClientId.Trim();
        if (await db.Clients.AnyAsync(c => c.ClientId == clientId, ct))
            throw ApiError.Conflict($"client_id '{clientId}' is already registered.");

        // Confidential clients always get a generated secret (FR-C02) — admins
        // never type one in, so nothing guessable ever enters the store.
        var secret = type == ClientType.Confidential ? Base64Url(RandomNumberGenerator.GetBytes(32)) : null;
        var client = new Client
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            Name = req.Name.Trim(),
            Type = type,
            SecretHash = secret is null ? null : Hash(secret),
            RedirectUris = [.. req.RedirectUris],
            AllowedScopes = [.. req.AllowedScopes ?? DefaultScopes],
            SkipConsent = req.SkipConsent ?? true,
            Description = Clean(req.Description),
            LaunchUrl = CleanUrl(req.LaunchUrl),
            RequireAssignment = req.RequireAssignment ?? false,
            Active = true,
            CreatedAt = clock.UtcNow,
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditEvents.ClientCreated, actorId, client.Id,
            ip: ip, userAgent: userAgent, metadata: new { client.ClientId }, ct: ct);
        return new CreatedClientResult(ToDto(client), secret);
    }

    public async Task<ClientDto> UpdateAsync(
        Guid id, UpdateClientRequest req, Guid actorId, IPAddress? ip, string? userAgent,
        CancellationToken ct = default)
    {
        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw ApiError.NotFound("Client not found.");

        if (req.Name is not null) client.Name = req.Name.Trim();
        if (req.RedirectUris is not null)
        {
            if (req.RedirectUris.Length == 0)
                throw ApiError.Validation("At least one redirect_uri is required.");
            client.RedirectUris = [.. req.RedirectUris];
        }
        if (req.AllowedScopes is not null) client.AllowedScopes = [.. req.AllowedScopes];
        if (req.SkipConsent is { } skip) client.SkipConsent = skip;
        if (req.Active is { } active) client.Active = active;
        if (req.Description is { } description) client.Description = Clean(description);
        if (req.LaunchUrl is { } launchUrl) client.LaunchUrl = CleanUrl(launchUrl);
        if (req.RequireAssignment is { } required) client.RequireAssignment = required;

        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditEvents.ClientUpdated, actorId, client.Id,
            ip: ip, userAgent: userAgent, ct: ct);
        return ToDto(client);
    }

    /// <summary>FR-C02 — rotate returns the new plaintext once; old secret dies immediately.</summary>
    public async Task<CreatedClientResult> RotateSecretAsync(
        Guid id, Guid actorId, IPAddress? ip, string? userAgent,
        CancellationToken ct = default)
    {
        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw ApiError.NotFound("Client not found.");
        if (client.Type != ClientType.Confidential)
            throw ApiError.Validation("Public clients do not carry secrets.");

        var secret = Base64Url(RandomNumberGenerator.GetBytes(32));
        client.SecretHash = Hash(secret);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditEvents.ClientSecretRotated, actorId, client.Id,
            ip: ip, userAgent: userAgent, ct: ct);
        return new CreatedClientResult(ToDto(client), secret);
    }

    private static ClientDto ToDto(Client c)
        => new(c.Id, c.ClientId, c.Name, c.Type.ToString().ToLowerInvariant(), c.Active,
            [.. c.RedirectUris], [.. c.AllowedScopes], c.SkipConsent,
            c.LaunchUrl, c.Description, c.RequireAssignment, c.CreatedAt);

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? CleanUrl(string? url)
    {
        var v = Clean(url);
        if (v is not null
            && (!Uri.TryCreate(v, UriKind.Absolute, out var u)
                || (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp)))
            throw ApiError.Validation("launch_url must be an absolute http(s) URL.");
        return v;
    }

    private static ClientType ParseType(string type) => type.ToLowerInvariant() switch
    {
        "confidential" => ClientType.Confidential,
        "public" => ClientType.Public,
        _ => throw ApiError.Validation("type must be 'confidential' or 'public'."),
    };

    private static string Hash(string secret)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
