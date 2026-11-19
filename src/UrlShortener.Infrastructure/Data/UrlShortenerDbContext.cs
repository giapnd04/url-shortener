using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace UrlShortener.Infrastructure.Data;

public sealed class UrlShortenerDbContext(DbContextOptions<UrlShortenerDbContext> options) : DbContext(options)
{
    public DbSet<ApiClient> ApiClients => Set<ApiClient>();
    public DbSet<ShortLink> ShortLinks => Set<ShortLink>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(UrlShortenerDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // All times are stored as UTC in datetime2, which has no offset. Mark values read back
        // as Utc so JSON serialises them with a trailing "Z" (API contract: ISO 8601 UTC).
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
