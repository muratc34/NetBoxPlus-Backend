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

    /// <summary>Length of a language code column.</summary>
    public const int LanguageMaxLength = 8;

    public static void MapTranslation<TOwner>(OwnedNavigationBuilder<TOwner, ContentTranslation> translation, string table)
        where TOwner : class
    {
        translation.ToTable(table);
        translation.WithOwner().HasForeignKey(OwnerKey);
        translation.HasKey(OwnerKey, nameof(ContentTranslation.Language));
        translation.Property(item => item.Language).HasMaxLength(LanguageMaxLength);
        translation.Property(item => item.Title).HasMaxLength(ContentText.TitleMaxLength).IsRequired();
        translation.Property(item => item.Synopsis).HasMaxLength(ContentText.SynopsisMaxLength).IsRequired();
    }

    public static void MapContentItem<TContent>(this EntityTypeBuilder<TContent> builder, string translationsTable, string genresTable, string creditsTable)
        where TContent : ContentItem
    {
        builder.HasKey(content => content.Id);
        builder.Property(content => content.Id).ValueGeneratedNever();

        builder.ComplexProperty(content => content.Info, info =>
        {
            info.Property(value => value.OriginalTitle).HasColumnName(nameof(ContentInfo.OriginalTitle)).HasMaxLength(ContentInfo.OriginalTitleMaxLength);
            info.Property(value => value.ReleaseYear).HasColumnName(nameof(ContentInfo.ReleaseYear));
            info.Property(value => value.AgeRating).HasColumnName(nameof(ContentInfo.AgeRating)).HasConversion<string>().HasMaxLength(16);
        });

        builder.Property(content => content.Status).HasConversion<string>().HasMaxLength(16);
        builder.Ignore(content => content.IsPublished);
        builder.Property(content => content.Version).IsConcurrencyToken();
        builder.Property(content => content.PosterImage).HasMaxLength(ImageFileConfiguration.NameMaxLength);
        builder.Property(content => content.BackdropImage).HasMaxLength(ImageFileConfiguration.NameMaxLength);

        // Customer lists show what is published, newest first.
        builder.HasIndex(content => new { content.Status, content.PublishedAt });
        builder.HasQueryFilter(content => !content.IsDeleted);

        builder.OwnsMany(content => content.Translations, translation => MapTranslation(translation, translationsTable));
        builder.Navigation(content => content.Translations).UsePropertyAccessMode(PropertyAccessMode.Field);

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

    /// <summary>The video columns of a movie or an episode, prefixed <c>Video_</c>.</summary>
    public static void MapVideo(ComplexPropertyBuilder<ContentVideo> video)
    {
        video.Property(item => item.LatestState).HasConversion<string>().HasMaxLength(16);
        video.Ignore(item => item.IsPlayable);
    }
}
