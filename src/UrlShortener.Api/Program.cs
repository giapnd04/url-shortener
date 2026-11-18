using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using UrlShortener.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddInfrastructure();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Liveness: the process is up. No dependency checks, so a slow database
// never makes an orchestrator restart a healthy process.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

// Readiness: dependencies the service needs before it can take traffic.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(InfrastructureSetup.ReadyTag)
});

// Management API under /api/links (X-Api-Key) arrives on Day 16: US-01 create, US-03 manage.

// Public redirect GET /{code} (US-02) also arrives on Day 16, with a route constraint
// so it never matches /api or /health. See docs/03-api-contract.md.

app.Run();

// Lets WebApplicationFactory<Program> in the integration tests find the entry point.
public partial class Program;
