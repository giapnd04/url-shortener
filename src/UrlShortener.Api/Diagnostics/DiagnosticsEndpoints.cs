using UrlShortener.Infrastructure.Caching;
using UrlShortener.Infrastructure.Diagnostics;

namespace UrlShortener.Api.Diagnostics;

/// <summary>
/// Benchmark-only endpoints (US-04): read and reset the number of SQL commands EF Core sent,
/// so the harness can report "DB queries per request". Mapped only when
/// Diagnostics:ExposeQueryCounter is true; never enable it on a public deployment.
/// Two path segments, so the single-segment redirect route can never match them.
/// </summary>
public static class DiagnosticsEndpoints
{
    public const string ConfigKey = "Diagnostics:ExposeQueryCounter";

    public static IEndpointRouteBuilder MapDiagnosticsEndpoints(this IEndpointRouteBuilder app, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>(ConfigKey))
        {
            return app;
        }

        app.MapGet("/_diag/db-queries", (QueryCounter counter) => TypedResults.Ok(new { count = counter.Count }));
        app.MapPost("/_diag/db-queries/reset", (QueryCounter counter) =>
        {
            counter.Reset();
            return TypedResults.NoContent();
        });

        // Hit ratio for the benchmark report (US-10). Separate endpoints so the harness still works
        // against older images that only have /_diag/db-queries (the SQL baseline commit).
        app.MapGet("/_diag/cache", (CacheStats stats) =>
            TypedResults.Ok(new { hits = stats.Hits, misses = stats.Misses }));
        app.MapPost("/_diag/cache/reset", (CacheStats stats) =>
        {
            stats.Reset();
            return TypedResults.NoContent();
        });

        return app;
    }
}
