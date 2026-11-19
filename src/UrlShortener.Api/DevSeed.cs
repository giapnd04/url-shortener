using Microsoft.EntityFrameworkCore;
using UrlShortener.Infrastructure.Auth;
using UrlShortener.Infrastructure.Data;

namespace UrlShortener.Api;

/// <summary>
/// Development only. There is no endpoint to create API clients (out of scope), so when
/// Seed:DevApiKey is set (user-secrets) we apply migrations and make sure a "dev" client with
/// that key exists, so the API can be tried with curl.
/// </summary>
public static class DevSeed
{
    public static async Task RunAsync(WebApplication app)
    {
        var devKey = app.Configuration["Seed:DevApiKey"];
        if (!app.Environment.IsDevelopment() || string.IsNullOrWhiteSpace(devKey))
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<UrlShortenerDbContext>();
        await db.Database.MigrateAsync();

        var hash = ApiKeyHasher.Hash(devKey);
        if (!await db.ApiClients.AnyAsync(c => c.ApiKeyHash == hash))
        {
            db.ApiClients.Add(new ApiClient { Name = "dev", ApiKeyHash = hash, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
    }
}
