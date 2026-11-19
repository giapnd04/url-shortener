using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UrlShortener.Infrastructure.Auth;
using UrlShortener.Infrastructure.Data;

namespace UrlShortener.Api.Auth;

/// <summary>
/// Reads X-Api-Key, hashes it with SHA-256 and looks the client up by the unique hash index.
/// The raw key never touches the database. Missing, unknown or disabled key → 401.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    UrlShortenerDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";
    public const string ClientIdClaim = "client_id";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values)
            || values.Count != 1
            || string.IsNullOrWhiteSpace(values[0]))
        {
            return AuthenticateResult.NoResult();
        }

        var hash = ApiKeyHasher.Hash(values[0]!);
        var client = await db.ApiClients
            .AsNoTracking()
            .Where(c => c.ApiKeyHash == hash)
            .Select(c => new { c.Id, c.DisabledAt })
            .FirstOrDefaultAsync(Context.RequestAborted);

        if (client is null || client.DisabledAt is not null)
        {
            // Same message for both so the response does not reveal whether a key exists.
            return AuthenticateResult.Fail("Invalid API key.");
        }

        var identity = new ClaimsIdentity([new Claim(ClientIdClaim, client.Id.ToString())], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}

public static class ClaimsPrincipalExtensions
{
    public static int GetClientId(this ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(ApiKeyAuthenticationHandler.ClientIdClaim)
                  ?? throw new InvalidOperationException("Endpoint requires API key authentication."));
}
