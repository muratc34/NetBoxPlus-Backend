using Catalog.Domain.Genres;
using Catalog.Domain.People;

namespace Catalog.Domain.Tests;

public sealed class PersonAndGenreTests
{
    [Fact]
    public void Person_RejectsInvalidValues()
    {
        Assert.Equal(PersonErrors.NameRequired, Person.Create(" ", null).Error);
        Assert.Equal(PersonErrors.NameTooLong, Person.Create(new string('a', Person.NameMaxLength + 1), null).Error);
        Assert.Equal(PersonErrors.BiographyTooLong, Person.Create("Ada", new string('a', Person.BiographyMaxLength + 1)).Error);
    }

    [Fact]
    public void Person_Change_LeavesItUntouchedWhenInvalid()
    {
        var person = Person.Create(" Ada ", " Writer ").Value;
        Assert.Equal("Ada", person.Name);
        Assert.Equal("Writer", person.Biography);

        Assert.True(person.Change("", null).IsFailure);
        Assert.Equal("Ada", person.Name);
    }

    [Theory]
    [InlineData("ilk", "İlk")]
    [InlineData("ılık", "ILIK")]
    [InlineData("Bilim Kurgu", "BİLİM KURGU")]
    [InlineData(" dram ", "DRAM")]
    [InlineData("şiir", "ŞİİR")]
    public void Genre_Normalize_FollowsTurkishCase(string first, string second)
        => Assert.Equal(Genre.Normalize(first), Genre.Normalize(second));

    [Fact]
    public void Genre_Normalize_KeepsDottedAndDotlessApart()
        => Assert.NotEqual(Genre.Normalize("kısa"), Genre.Normalize("kisa"));

    [Fact]
    public void Genre_Rename_UpdatesTheNormalizedName()
    {
        var genre = Genre.Create("Komedi").Value;

        Assert.True(genre.Rename("Bilim Kurgu").IsSuccess);

        Assert.Equal(Genre.Normalize("bilim kurgu"), genre.NormalizedName);
    }

    [Fact]
    public void Genre_RejectsInvalidNames()
    {
        Assert.Equal(GenreErrors.NameRequired, Genre.Create(" ").Error);
        Assert.Equal(GenreErrors.NameTooLong, Genre.Create(new string('a', Genre.NameMaxLength + 1)).Error);
    }
}
