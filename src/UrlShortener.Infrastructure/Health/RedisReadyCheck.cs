using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using UrlShortener.Infrastructure.Caching;

namespace UrlShortener.Infrastructure.Health;

/// <summary>
/// Redis is an optimisation, not a dependency: when it is down redirects still work through SQL.
/// So a failure reports Degraded (still HTTP 200 on /health/ready), never Unhealthy, and the
/// orchestrator keeps sending traffic (US-08).
/// </summary>
public sealed class RedisReadyCheck(IServiceProvider services) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (services.GetRequiredService<ILinkCache>() is not RedisLinkCache)
        {
            return HealthCheckResult.Healthy("Redis cache is not enabled.");
        }

        try
        {
            await services.GetRequiredService<IConnectionMultiplexer>().GetDatabase().PingAsync();
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            return HealthCheckResult.Degraded("Redis is unreachable; redirects are served from SQL.", ex);
        }
    }
}
