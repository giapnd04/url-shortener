using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace UrlShortener.Infrastructure.Health;

/// <summary>
/// Readiness check for SQL Server, the source of truth. If SQL is unreachable the
/// service cannot create or resolve links, so the check reports Unhealthy (503).
/// Redis will get its own check in v0.2 and report Degraded, never Unhealthy.
/// </summary>
public sealed class SqlServerReadyCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString(InfrastructureSetup.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return HealthCheckResult.Unhealthy(
                $"Connection string '{InfrastructureSetup.ConnectionStringName}' is not configured.");
        }

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            // Do not put the exception message in the response: it can contain server names.
            return HealthCheckResult.Unhealthy("SQL Server is unreachable.", ex);
        }
    }
}
