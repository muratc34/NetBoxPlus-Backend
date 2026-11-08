using Catalog.Domain.Shows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Infrastructure.Persistence;

internal sealed class SeriesConfiguration : IEntityTypeConfiguration<Series>
{
    public void Configure(EntityTypeBuilder<Series> builder)
    {
        builder.ToTable("Series");
        builder.MapContentItem("SeriesGenres", "SeriesCredits");

        builder.HasMany(series => series.Seasons).WithOne().HasForeignKey("SeriesId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(series => series.Seasons).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class SeasonConfiguration : IEntityTypeConfiguration<Season>
{
    public void Configure(EntityTypeBuilder<Season> builder)
    {
        builder.ToTable("Seasons");
        builder.HasKey(season => season.Id);

        // The domain creates the ids: without this, EF Core would take a new child for an existing row.
        builder.Property(season => season.Id).ValueGeneratedNever();
        builder.Property(season => season.Title).HasMaxLength(Season.TitleMaxLength);
        builder.HasIndex("SeriesId", nameof(Season.Number)).IsUnique();

        builder.HasMany(season => season.Episodes).WithOne().HasForeignKey("SeasonId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(season => season.Episodes).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class EpisodeConfiguration : IEntityTypeConfiguration<Episode>
{
    public void Configure(EntityTypeBuilder<Episode> builder)
    {
        builder.ToTable("Episodes");
        builder.HasKey(episode => episode.Id);
        builder.Property(episode => episode.Id).ValueGeneratedNever();
        builder.Property(episode => episode.Title).HasMaxLength(Episode.TitleMaxLength);
        builder.Property(episode => episode.Synopsis).HasMaxLength(Episode.SynopsisMaxLength);
        builder.Property(episode => episode.Status).HasConversion<string>().HasMaxLength(16);
        builder.Ignore(episode => episode.IsPublished);
        builder.HasIndex("SeasonId", nameof(Episode.Number)).IsUnique();
    }
}
