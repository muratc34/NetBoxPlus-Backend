using Catalog.Domain.Popularity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Infrastructure.Persistence;

internal sealed class PopularityCopyConfiguration : IEntityTypeConfiguration<PopularityCopy>
{
    public void Configure(EntityTypeBuilder<PopularityCopy> builder)
    {
        builder.ToTable("PopularityCopies");
        builder.HasKey(copy => copy.Id);
        builder.Property(copy => copy.Id).ValueGeneratedNever();
        builder.Property(copy => copy.Revision).IsConcurrencyToken();
        builder.HasMany(copy => copy.Titles).WithOne().HasForeignKey(title => title.CopyId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(copy => copy.Titles).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class TitlePopularityConfiguration : IEntityTypeConfiguration<TitlePopularity>
{
    public void Configure(EntityTypeBuilder<TitlePopularity> builder)
    {
        builder.ToTable("TitlePopularity");
        builder.HasKey(title => new { title.CopyId, title.TitleId });
        builder.HasIndex(title => title.TitleId).IsUnique();
    }
}

internal sealed class PopularityRepository(CatalogDbContext context) : IPopularityRepository
{
    public Task<PopularityCopy?> GetAsync(CancellationToken cancellationToken)
        => context.PopularityCopies.Include(copy => copy.Titles).FirstOrDefaultAsync(copy => copy.Id == PopularityCopy.SingletonId, cancellationToken);

    public void Add(PopularityCopy copy) => context.PopularityCopies.Add(copy);
}
