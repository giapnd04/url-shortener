using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UrlShortener.Infrastructure.Data.Configurations;

internal sealed class ApiClientConfiguration : IEntityTypeConfiguration<ApiClient>
{
    public void Configure(EntityTypeBuilder<ApiClient> builder)
    {
        builder.ToTable("api_clients");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.ApiKeyHash).HasColumnName("api_key_hash")
            .HasColumnType("binary(32)").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.DisabledAt).HasColumnName("disabled_at");

        // Every authenticated request looks the client up by key hash, so this must be an index seek.
        builder.HasIndex(x => x.ApiKeyHash).IsUnique().HasDatabaseName("UX_api_clients_api_key_hash");
    }
}
