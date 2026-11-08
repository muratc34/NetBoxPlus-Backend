using Catalog.Domain.Content;
using Catalog.Domain.Movies;

namespace Catalog.Domain.Tests;

public sealed class ContentInfoTests
{
    [Fact]
    public void Info_DropsABlankOriginalTitle()
        => Assert.Null(ContentInfo.Create("   ", 2024, AgeRating.Age7).Value.OriginalTitle);

    [Theory]
    [InlineData(ContentInfo.MinReleaseYear - 1)]
    [InlineData(ContentInfo.MaxReleaseYear + 1)]
    public void Info_RejectsAYearOutOfRange(int year)
        => Assert.Equal(ContentErrors.ReleaseYearOutOfRange, ContentInfo.Create(null, year, AgeRating.All).Error);

    [Theory]
    [InlineData(ContentInfo.MinReleaseYear)]
    [InlineData(ContentInfo.MaxReleaseYear)]
    public void Info_AcceptsTheYearLimits(int year)
        => Assert.True(ContentInfo.Create(null, year, AgeRating.All).IsSuccess);

    [Fact]
    public void Info_RejectsATooLongOriginalTitle_AndAnUnknownRating()
    {
        Assert.Equal(ContentErrors.OriginalTitleTooLong, ContentInfo.Create(new string('a', ContentInfo.OriginalTitleMaxLength + 1), 2024, AgeRating.All).Error);
        Assert.Equal(ContentErrors.AgeRatingInvalid, ContentInfo.Create(null, 2024, (AgeRating)99).Error);
    }

    [Fact]
    public void Text_IsTrimmed()
    {
        var text = ContentText.Create("  Night Train ", " Journey ").Value;

        Assert.Equal("Night Train", text.Title);
        Assert.Equal("Journey", text.Synopsis);
    }

    [Theory]
    [InlineData(null, "Text", "Content.TitleRequired")]
    [InlineData("  ", "Text", "Content.TitleRequired")]
    [InlineData("Title", null, "Content.SynopsisRequired")]
    public void Text_RejectsMissingValues(string? title, string? synopsis, string code)
        => Assert.Equal(code, ContentText.Create(title, synopsis).Error.Code);

    [Fact]
    public void Text_RejectsTooLongValues()
    {
        Assert.Equal(ContentErrors.TitleTooLong, ContentText.Create(new string('a', ContentText.TitleMaxLength + 1), "Text").Error);
        Assert.Equal(ContentErrors.SynopsisTooLong, ContentText.Create("Title", new string('a', ContentText.SynopsisMaxLength + 1)).Error);
    }

    [Fact]
    public void Texts_NeedTheDefaultLanguage_AndOnlySupportedOnes()
    {
        var english = ContentText.Create("Title", "Text").Value;

        Assert.Equal(ContentErrors.DefaultLanguageRequired, Movie.Create(TestContent.Info(), new Dictionary<string, ContentText> { ["en"] = english }, 120).Error);
        Assert.Equal(ContentErrors.UnsupportedLanguage, Movie.Create(TestContent.Info(), new Dictionary<string, ContentText> { ["tr"] = english, ["fr"] = english }, 120).Error);
    }

    [Fact]
    public void TextFor_FallsBackToTheDefaultLanguage()
    {
        var movie = Movie.Create(TestContent.Info(), TestContent.Texts("Gece Treni", "Night Train"), 120).Value;
        var turkishOnly = TestContent.Movie();

        Assert.Equal("Night Train", movie.TextFor("en").Title);
        Assert.Equal("Gece Treni", movie.TextFor("tr").Title);
        Assert.Equal("tr", turkishOnly.TextFor("en").Language);
    }

    [Fact]
    public void SetTexts_KeepsEntries_UpdatesThem_AndDropsRemovedLanguages()
    {
        var movie = Movie.Create(TestContent.Info(), TestContent.Texts("Eski", "Old"), 120).Value;
        var turkish = movie.Translations.Single(item => item.Language == "tr");

        Assert.True(movie.SetTexts(TestContent.Texts("Yeni")).IsSuccess);

        Assert.Same(turkish, Assert.Single(movie.Translations));
        Assert.Equal("Yeni", turkish.Title);
    }
}
