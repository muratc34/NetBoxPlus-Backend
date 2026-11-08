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
        builder.Property(person => person.Biography).HasMaxLength(Person.BiographyMaxLength);
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
        builder.Property(genre => genre.Name).HasMaxLength(Genre.NameMaxLength).IsRequired();
        builder.Property(genre => genre.NormalizedName).HasMaxLength(Genre.NameMaxLength).IsRequired();
        builder.HasIndex(genre => genre.NormalizedName).IsUnique();
    }
}
