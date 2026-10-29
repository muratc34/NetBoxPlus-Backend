using Feed.Domain.Items;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Feed.Infrastructure.Persistence;

internal sealed class FeedItemConfiguration : IEntityTypeConfiguration<FeedItem>
{
    public void Configure(EntityTypeBuilder<FeedItem> builder)
    {
        builder.ToTable("FeedItems");
        builder.HasKey(item => item.Id);

        // One item per note, enforced by the database as well as by the handler.
        builder.HasIndex(item => item.NoteId).IsUnique();

        builder.Property(item => item.Title).HasMaxLength(FeedItem.TitleMaxLength).IsRequired();

        // The feed is read newest first.
        builder.HasIndex(item => item.CreatedAt);
    }
}
