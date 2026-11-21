using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using UrlShortener.Api;
using UrlShortener.Api.Auth;
using UrlShortener.Api.Diagnostics;
using UrlShortener.Api.Errors;
using UrlShortener.Api.Links;
using UrlShortener.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<CodeGenerationExhaustedHandler>();
builder.Services.AddInfrastructure();
builder.Services.AddSingleton<PublicUrl>();

builder.Services
    .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorization();

var app = builder.Build();

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

await DevSeed.RunAsync(app);

app.Run();

// Lets WebApplicationFactory<Program> in the integration tests find the entry point.
public partial class Program;
