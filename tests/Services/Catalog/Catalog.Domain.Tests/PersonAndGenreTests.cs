using Catalog.Domain.Content;
using Catalog.Domain.Genres;
using Catalog.Domain.People;

namespace Catalog.Domain.Tests;

public sealed class PersonAndGenreTests
{
    private static Dictionary<string, string?> Names(string? turkish, string? english = null)
    {
        var names = new Dictionary<string, string?> { ["tr"] = turkish };
        if (english is not null)
        {
            names["en"] = english;
        }

        return names;
    }

    [Fact]
    public void Person_RejectsInvalidValues()
    {
        Assert.Equal(PersonErrors.NameRequired, Person.Create(" ", null).Error);
        Assert.Equal(PersonErrors.NameTooLong, Person.Create(new string('a', Person.NameMaxLength + 1), null).Error);
        Assert.Equal(PersonErrors.BiographyTooLong, Person.Create("Ada", Names(new string('a', Person.BiographyMaxLength + 1))).Error);
        Assert.Equal(ContentErrors.UnsupportedLanguage, Person.Create("Ada", new Dictionary<string, string?> { ["fr"] = "Bio" }).Error);
    }

    [Fact]
    public void Person_Change_LeavesItUntouchedWhenInvalid()
    {
        var person = Person.Create(" Ada ", Names(" Yazar ")).Value;
        Assert.Equal("Ada", person.Name);
        Assert.Equal("Yazar", person.BiographyFor("tr")!.Biography);

        Assert.True(person.Change("", null).IsFailure);
        Assert.Equal("Ada", person.Name);
    }

    [Fact]
    public void Person_BiographyFallsBack_AndMayBeMissing()
    {
        Assert.Equal("tr", Person.Create("Ada", Names("Yazar")).Value.BiographyFor("en")!.Language);
        Assert.Null(Person.Create("Ada", null).Value.BiographyFor("en"));
    }

    [Theory]
    [InlineData("ilk", "İlk")]
    [InlineData("ılık", "ILIK")]
    [InlineData("Bilim Kurgu", "BİLİM KURGU")]
    [InlineData(" dram ", "DRAM")]
    [InlineData("şiir", "ŞİİR")]
    public void Genre_Normalize_FollowsTurkishCase(string first, string second)
        => Assert.Equal(Genre.Normalize("tr", first), Genre.Normalize("tr", second));

    [Fact]
    public void Genre_Normalize_KeepsDottedAndDotlessApart()
        => Assert.NotEqual(Genre.Normalize("tr", "kısa"), Genre.Normalize("tr", "kisa"));

    [Fact]
    public void Genre_Normalize_UsesPlainCaseInEnglish()
        => Assert.Equal("FICTION", Genre.Normalize("en", "fiction"));

    [Fact]
    public void Genre_Rename_UpdatesTheNormalizedName()
    {
        var genre = Genre.Create(Names("Komedi", "Comedy")).Value;

        Assert.True(genre.Rename(Names("Bilim Kurgu")).IsSuccess);

        var name = Assert.Single(genre.Translations);
        Assert.Equal(Genre.Normalize("tr", "bilim kurgu"), name.NormalizedName);
    }

    [Fact]
    public void Genre_NameFor_FallsBackToTheDefaultLanguage()
    {
        Assert.Equal("Komedi", Genre.Create(Names("Komedi")).Value.NameFor("en"));
        Assert.Equal("Comedy", Genre.Create(Names("Komedi", "Comedy")).Value.NameFor("en"));
    }

    [Fact]
    public void Genre_RejectsInvalidNames()
    {
        Assert.Equal(GenreErrors.NameRequired, Genre.Create(Names(" ")).Error);
        Assert.Equal(GenreErrors.NameRequired, Genre.Create(new Dictionary<string, string?> { ["en"] = "Comedy" }).Error);
        Assert.Equal(GenreErrors.NameTooLong, Genre.Create(Names(new string('a', Genre.NameMaxLength + 1))).Error);
        Assert.Equal(ContentErrors.UnsupportedLanguage, Genre.Create(new Dictionary<string, string?> { ["tr"] = "Dram", ["fr"] = "Drame" }).Error);
    }
}
