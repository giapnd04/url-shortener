using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UrlShortener.Infrastructure.Data.Configurations;

public sealed class ClickEventConfiguration : IEntityTypeConfiguration<ClickEvent>
{
    public const string LinkIdClickedAtIndex = "IX_click_events_link_id_clicked_at";

    public void Configure(EntityTypeBuilder<ClickEvent> builder)
    {
        builder.ToTable("click_events");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.LinkId).HasColumnName("link_id");
        builder.Property(x => x.ClickedAt).HasColumnName("clicked_at");
        builder.Property(x => x.ReferrerHost).HasColumnName("referrer_host").HasMaxLength(255).IsUnicode(false);
        builder.Property(x => x.UaFamily).HasColumnName("ua_family").HasMaxLength(50).IsUnicode(false);

        // Stats always filter by one link and a time range: equality column first, range column second,
        // so "link_id = @id AND clicked_at >= @from AND clicked_at < @to" is a single range seek.
        builder.HasIndex(x => new { x.LinkId, x.ClickedAt }).HasDatabaseName(LinkIdClickedAtIndex);

        builder.HasOne<ShortLink>().WithMany()
            .HasForeignKey(x => x.LinkId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
