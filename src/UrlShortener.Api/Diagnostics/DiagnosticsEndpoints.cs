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

        return app;
    }
}
