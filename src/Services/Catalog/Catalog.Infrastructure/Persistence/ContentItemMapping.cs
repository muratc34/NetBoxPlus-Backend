using Catalog.Domain.Content;
using Catalog.Domain.Genres;
using Catalog.Domain.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Infrastructure.Persistence;

/// <summary>The mapping movies and series share; each gets its own genre and credit tables.</summary>
internal static class ContentItemMapping
{
    private const string OwnerKey = "ContentId";

    public static void MapContentItem<TContent>(this EntityTypeBuilder<TContent> builder, string genresTable, string creditsTable)
        where TContent : ContentItem
    {
        builder.HasKey(content => content.Id);
        builder.Property(content => content.Id).ValueGeneratedNever();

        builder.ComplexProperty(content => content.Info, info =>
        {
            info.Property(value => value.Title).HasColumnName(nameof(ContentInfo.Title)).HasMaxLength(ContentInfo.TitleMaxLength);
            info.Property(value => value.OriginalTitle).HasColumnName(nameof(ContentInfo.OriginalTitle)).HasMaxLength(ContentInfo.TitleMaxLength);
            info.Property(value => value.Synopsis).HasColumnName(nameof(ContentInfo.Synopsis)).HasMaxLength(ContentInfo.SynopsisMaxLength);
            info.Property(value => value.ReleaseYear).HasColumnName(nameof(ContentInfo.ReleaseYear));
            info.Property(value => value.AgeRating).HasColumnName(nameof(ContentInfo.AgeRating)).HasConversion<string>().HasMaxLength(16);
        });

        builder.Property(content => content.Status).HasConversion<string>().HasMaxLength(16);
        builder.Ignore(content => content.IsPublished);
        builder.Property(content => content.Version).IsConcurrencyToken();

        // Customer lists show what is published, newest first.
        builder.HasIndex(content => new { content.Status, content.PublishedAt });
        builder.HasQueryFilter(content => !content.IsDeleted);

        builder.OwnsMany(content => content.Genres, genre =>
        {
            genre.ToTable(genresTable);
            genre.WithOwner().HasForeignKey(OwnerKey);
            genre.HasKey(OwnerKey, nameof(ContentGenre.GenreId));
            genre.HasOne<Genre>().WithMany().HasForeignKey(item => item.GenreId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Navigation(content => content.Genres).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(content => content.Credits, credit =>
        {
            credit.ToTable(creditsTable);
            credit.WithOwner().HasForeignKey(OwnerKey);
            credit.HasKey(OwnerKey, nameof(Credit.PersonId), nameof(Credit.Role));
            credit.Property(item => item.Role).HasConversion<string>().HasMaxLength(16);
            credit.Property(item => item.CharacterName).HasMaxLength(Credit.CharacterNameMaxLength);
            credit.HasOne<Person>().WithMany().HasForeignKey(item => item.PersonId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Navigation(content => content.Credits).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
