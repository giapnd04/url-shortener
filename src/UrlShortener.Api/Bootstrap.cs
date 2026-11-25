using Microsoft.EntityFrameworkCore;
using UrlShortener.Infrastructure.Auth;
using UrlShortener.Infrastructure.Data;

namespace UrlShortener.Api;

/// <summary>
/// Runs once at startup, before the app takes traffic.
///
/// 1. Database:MigrateOnStartup=true applies EF migrations. docker-compose turns it on, so a fresh
///    clone + `docker compose up` gets a working database instead of a 503 readiness forever.
///    Retried a few times because SQL Server can report healthy slightly before it accepts logins.
/// 2. Bootstrap:ApiKey creates one API client with that key if none exists. There is no endpoint
///    for creating clients (out of scope), so this is how the first caller gets in. In Development
///    the older Seed:DevApiKey (user-secrets) still works and also implies migrating.
///
/// With several instances, migrating at startup races; a real deployment would run migrations as a
/// separate step (an idempotent script, like benchmarks/sql/migrate.sql). Fine for one instance.
/// </summary>
public static class Bootstrap
{
    public static async Task RunAsync(WebApplication app)
    {
        var config = app.Configuration;
        var devKey = app.Environment.IsDevelopment() ? config["Seed:DevApiKey"] : null;
        var apiKey = config["Bootstrap:ApiKey"];
        var clientName = config["Bootstrap:ClientName"] ?? "bootstrap";
        if (string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(devKey))
        {
            apiKey = devKey;
            clientName = "dev";
        }

        var migrate = config.GetValue<bool>("Database:MigrateOnStartup") || !string.IsNullOrWhiteSpace(devKey);
        if (!migrate && string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(Bootstrap));
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<UrlShortenerDbContext>();

        if (migrate)
        {
            await MigrateWithRetryAsync(db, logger);
        }

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var hash = ApiKeyHasher.Hash(apiKey);
            if (!await db.ApiClients.AnyAsync(c => c.ApiKeyHash == hash))
            {
                db.ApiClients.Add(new ApiClient { Name = clientName, ApiKeyHash = hash, CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
                // Never log the key itself.
                logger.LogInformation("Created bootstrap API client {ClientName}", clientName);
            }
        }
    }

    private static async Task MigrateWithRetryAsync(UrlShortenerDbContext db, ILogger logger)
    {
        const int attempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync();
                logger.LogInformation("Database migrations applied");
                return;
            }
            catch (Exception ex) when (attempt < attempts)
            {
                logger.LogWarning(ex, "Migration attempt {Attempt}/{Attempts} failed; retrying", attempt, attempts);
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
            }
        }
    }
}
