using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using UrlShortener.Infrastructure.Analytics;
using UrlShortener.Infrastructure.Caching;
using UrlShortener.Infrastructure.Data;
using UrlShortener.Infrastructure.Diagnostics;
using UrlShortener.Infrastructure.Health;
using UrlShortener.Infrastructure.Links;

namespace UrlShortener.Infrastructure;

public static class InfrastructureSetup
{
    public const string ConnectionStringName = "UrlShortener";

    /// <summary>Tag used by /health/ready to select dependency checks.</summary>
    public const string ReadyTag = "ready";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<QueryCounter>();
        services.AddSingleton<QueryCountingInterceptor>();
        services.AddSingleton<IShortCodeGenerator, RandomBase62CodeGenerator>();
        services.AddScoped<LinkService>();
        services.AddSingleton<CacheStats>();
        services.AddOptions<AnalyticsOptions>().BindConfiguration(AnalyticsOptions.Section);
        services.AddScoped<ClickRecorder>();
        services.AddScoped<ClickStatsService>();

        // Resolve the connection string lazily so tests can override configuration.
        services.AddDbContext<UrlShortenerDbContext>((sp, options) =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");
            }

            options.UseSqlServer(connectionString)
                .AddInterceptors(sp.GetRequiredService<QueryCountingInterceptor>());
        });

        AddCaching(services);

        services.AddHealthChecks()
            .AddCheck<SqlServerReadyCheck>("sqlserver", tags: [ReadyTag])
            .AddCheck<RedisReadyCheck>("redis", tags: [ReadyTag]);

        return services;
    }

    private static void AddCaching(IServiceCollection services)
    {
        services.AddOptions<CacheOptions>().BindConfiguration(CacheOptions.Section);

        // Singleton: one multiplexer per process. It is thread-safe, multiplexes all commands over a few
        // connections and is expensive to create; one per request would exhaust connections.
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString(CacheOptions.RedisConnectionStringName)
                ?? throw new InvalidOperationException("ConnectionStrings:Redis is not configured.");
            var options = ConfigurationOptions.Parse(connectionString);
            // US-08: never block startup on Redis, and keep timeouts short so a dead Redis
            // costs a request at most a few hundred ms before it falls back to SQL.
            options.AbortOnConnectFail = false;
            options.ConnectTimeout = 1000;
            options.SyncTimeout = 250;
            options.AsyncTimeout = 250;
            // Retry every second instead of the default exponential backoff, so the cache is
            // picked up again soon after Redis comes back (no API restart needed).
            options.ReconnectRetryPolicy = new LinearRetry(1000);
            // While disconnected, fail commands at once instead of queueing them in the backlog until a
            // reconnect attempt finishes. Measured in compose before this: 1-4 s per redirect with Redis
            // stopped. A cache that is down should cost ~nothing; the request just goes to SQL.
            options.BacklogPolicy = BacklogPolicy.FailFast;
            return ConnectionMultiplexer.Connect(options);
        });

        // Strategy chosen from configuration, resolved lazily (test configuration applies at Build time).
        services.AddSingleton<ILinkCache>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var options = sp.GetRequiredService<IOptions<CacheOptions>>().Value;
            var time = sp.GetRequiredService<TimeProvider>();
            var provider = options.Provider
                ?? (string.IsNullOrWhiteSpace(config.GetConnectionString(CacheOptions.RedisConnectionStringName))
                    ? CacheProvider.None
                    : CacheProvider.Redis);

            return provider switch
            {
                CacheProvider.Redis => new RedisLinkCache(
                    sp.GetRequiredService<IConnectionMultiplexer>(), time,
                    sp.GetRequiredService<ILogger<RedisLinkCache>>()),
                CacheProvider.Memory => new InMemoryLinkCache(options.MemoryCapacity, time),
                _ => new NullLinkCache(),
            };
        });

        // Decorator wiring: the endpoint asks for IRedirectReader and gets SQL wrapped in cache-aside,
        // or plain SQL when caching is off (so mode A of the benchmark has no extra hop).
        services.AddScoped<SqlRedirectReader>();
        services.AddScoped<IRedirectReader>(sp =>
        {
            var sql = sp.GetRequiredService<SqlRedirectReader>();
            var cache = sp.GetRequiredService<ILinkCache>();
            return cache is NullLinkCache
                ? sql
                : new CachedRedirectReader(sql, cache, sp.GetRequiredService<TimeProvider>(),
                    sp.GetRequiredService<IOptions<CacheOptions>>(), sp.GetRequiredService<CacheStats>());
        });
    }
}
