using Catalog.Application.Abstractions;
using Catalog.Domain.Content;
using Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Infrastructure.Search;

/// <summary>
/// A title waiting to be brought up to date in the search index. Written with the change that caused
/// it, and kept until the index has taken it, however long the index is away. A title can have
/// several rows at once: inserting never conflicts, and the worker handles them together.
/// </summary>
internal sealed class SearchIndexEntry
{
    public Guid Id { get; init; }

    public Guid ContentId { get; init; }

    public ContentKind Kind { get; init; }

    public DateTimeOffset RequestedAt { get; init; }
}

internal sealed class SearchIndexEntryConfiguration : IEntityTypeConfiguration<SearchIndexEntry>
{
    public void Configure(EntityTypeBuilder<SearchIndexEntry> builder)
    {
        builder.ToTable("SearchIndexQueue");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.Kind).HasConversion<string>().HasMaxLength(16);
    }
}

internal sealed class EfSearchQueue(CatalogDbContext context, TimeProvider timeProvider) : ISearchQueue
{
    public void Enqueue(Guid contentId, ContentKind kind)
        => context.Set<SearchIndexEntry>().Add(new SearchIndexEntry
        {
            Id = Guid.CreateVersion7(),
            ContentId = contentId,
            Kind = kind,
            RequestedAt = timeProvider.GetUtcNow(),
        });
}
