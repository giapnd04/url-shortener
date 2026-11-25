using System.Diagnostics;
using System.Text.RegularExpressions;
using UrlShortener.Infrastructure.Caching;

namespace UrlShortener.Api.Observability;

/// <summary>
/// US-12 structured logging, one middleware so the order is obvious:
/// 1. Correlation id: taken from X-Correlation-Id when it looks safe, otherwise a new one. It is
///    echoed back, used as HttpContext.TraceIdentifier (so ProblemDetails.traceId matches the logs),
///    and put in a log scope, so every log line of the request carries it.
/// 2. One completion line per request: method, route template (not the raw path, low cardinality),
///    status, elapsed ms and CacheResult (hit / miss / error / none). Health probes log at Debug.
/// </summary>
public sealed partial class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context, RedirectDiagnostics diagnostics)
    {
        var correlationId = ResolveCorrelationId(context.Request.Headers[HeaderName]);
        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using var scope = logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId });
        var started = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(no route)";
            var level = context.Request.Path.StartsWithSegments("/health") ? LogLevel.Debug : LogLevel.Information;
            logger.Log(level,
                "HTTP {Method} {Route} responded {StatusCode} in {ElapsedMs:0.0} ms, cache {CacheResult}",
                context.Request.Method, route, context.Response.StatusCode, elapsedMs,
                diagnostics.CacheResult?.ToString().ToLowerInvariant() ?? "none");
        }
    }

    /// <summary>Accept a caller's id only if it is short and plain, so it cannot inject into logs or headers.</summary>
    public static string ResolveCorrelationId(string? incoming) =>
        !string.IsNullOrEmpty(incoming) && SafeId().IsMatch(incoming) ? incoming : Guid.NewGuid().ToString("N");

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex SafeId();
}
