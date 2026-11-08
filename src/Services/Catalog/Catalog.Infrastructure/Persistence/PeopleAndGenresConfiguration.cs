using Catalog.Domain.Genres;
using Catalog.Domain.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Infrastructure.Persistence;

internal sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("People");
        builder.HasKey(person => person.Id);
        builder.Property(person => person.Id).ValueGeneratedNever();
        builder.Property(person => person.Name).HasMaxLength(Person.NameMaxLength).IsRequired();
        builder.OwnsMany(person => person.Translations, translation =>
        {
            translation.ToTable("PersonTranslations");
            translation.WithOwner().HasForeignKey("PersonId");
            translation.HasKey("PersonId", nameof(PersonTranslation.Language));
            translation.Property(item => item.Language).HasMaxLength(ContentItemMapping.LanguageMaxLength);
            translation.Property(item => item.Biography).HasMaxLength(Person.BiographyMaxLength).IsRequired();
        });
        builder.Navigation(person => person.Translations).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Property(person => person.PhotoImage).HasMaxLength(ImageFileConfiguration.NameMaxLength);
    }
}

internal sealed class GenreConfiguration : IEntityTypeConfiguration<Genre>
{
    public void Configure(EntityTypeBuilder<Genre> builder)
    {
        builder.ToTable("Genres");
        builder.HasKey(genre => genre.Id);
        builder.Property(genre => genre.Id).ValueGeneratedNever();
        builder.OwnsMany(genre => genre.Translations, translation =>
        {
            translation.ToTable("GenreTranslations");
            translation.WithOwner().HasForeignKey("GenreId");
            translation.HasKey("GenreId", nameof(GenreTranslation.Language));
            translation.Property(item => item.Language).HasMaxLength(ContentItemMapping.LanguageMaxLength);
            translation.Property(item => item.Name).HasMaxLength(Genre.NameMaxLength).IsRequired();
            translation.Property(item => item.NormalizedName).HasMaxLength(Genre.NameMaxLength).IsRequired();
            translation.HasIndex(item => new { item.Language, item.NormalizedName }).IsUnique();
        });
        builder.Navigation(genre => genre.Translations).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
