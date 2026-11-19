using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        services.AddHealthChecks()
            .AddCheck<SqlServerReadyCheck>("sqlserver", tags: [ReadyTag]);

        return services;
    }
}
