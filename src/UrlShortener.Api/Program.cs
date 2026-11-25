using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using UrlShortener.Api;
using UrlShortener.Api.Auth;
using UrlShortener.Api.Diagnostics;
using UrlShortener.Api.Errors;
using UrlShortener.Api.Links;
using UrlShortener.Api.Observability;
using UrlShortener.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Structured JSON logs (with scopes, so CorrelationId is on every line) everywhere except local dev,
// where the plain console format is easier to read.
if (!builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(o =>
    {
        o.IncludeScopes = true;
        o.UseUtcTimestamp = true;
        o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    });
}

// traceId in every ProblemDetails = the correlation id set by RequestLoggingMiddleware,
// so a client can quote it and we can find the request in the logs.
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
    ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier);
// Order matters: specific handlers first, the catch-all last.
builder.Services.AddExceptionHandler<CodeGenerationExhaustedHandler>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddInfrastructure();
builder.Services.AddSingleton<PublicUrl>();

builder.Services
    .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorization();

var app = builder.Build();

// Outermost, so the correlation id and the completion line cover errors handled below it.
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();

// Liveness: the process is up. No dependency checks, so a slow database
// never makes an orchestrator restart a healthy process.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

// Readiness: dependencies the service needs before it can take traffic.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(InfrastructureSetup.ReadyTag)
});

// Management API under /api/links (X-Api-Key) and public redirect GET /{code}.
// See docs/03-api-contract.md.
app.MapLinkEndpoints();

// Benchmark harness only (US-04), off unless Diagnostics:ExposeQueryCounter=true.
app.MapDiagnosticsEndpoints(app.Configuration);

await Bootstrap.RunAsync(app);

app.Run();

// Lets WebApplicationFactory<Program> in the integration tests find the entry point.
public partial class Program;
