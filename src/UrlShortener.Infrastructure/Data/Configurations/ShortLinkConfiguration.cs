using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UrlShortener.Infrastructure.Data.Configurations;

public sealed class ShortLinkConfiguration : IEntityTypeConfiguration<ShortLink>
{
    /// <summary>
    /// Byte-wise, case-sensitive comparison. The server default (SQL_Latin1_General_CP1_CI_AS)
    /// would treat "abc" and "ABC" as equal, which breaks Base62. See docs/02-database-design.md.
    /// </summary>
    public const string CodeCollation = "Latin1_General_100_BIN2";

    public const string UniqueCodeIndex = "UX_short_links_code";

    public void Configure(EntityTypeBuilder<ShortLink> builder)
    {
        builder.ToTable("short_links");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Code).HasColumnName("code")
            .HasMaxLength(16).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        builder.Property(x => x.TargetUrl).HasColumnName("target_url").HasMaxLength(2048).IsRequired();
        builder.Property(x => x.ClientId).HasColumnName("client_id");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.Property(x => x.DisabledAt).HasColumnName("disabled_at");

        // The unique index is the real (atomic) collision detector, and the redirect seek path.
        builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName(UniqueCodeIndex);
        builder.HasIndex(x => new { x.ClientId, x.CreatedAt }).HasDatabaseName("IX_short_links_client_id_created_at");

        builder.HasOne(x => x.Client).WithMany()
            .HasForeignKey(x => x.ClientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
