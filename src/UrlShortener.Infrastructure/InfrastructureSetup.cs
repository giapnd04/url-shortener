using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Infrastructure.Health;

namespace UrlShortener.Infrastructure;

public static class InfrastructureSetup
{
    public const string ConnectionStringName = "UrlShortener";

    /// <summary>Tag used by /health/ready to select dependency checks.</summary>
    public const string ReadyTag = "ready";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // Day 16 adds the DbContext and repositories here.
        services.AddHealthChecks()
            .AddCheck<SqlServerReadyCheck>("sqlserver", tags: [ReadyTag]);

        return services;
    }
}
