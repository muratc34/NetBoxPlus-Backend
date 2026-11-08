using Catalog.Domain.Content;
using Shared.Domain;

namespace Catalog.Domain.Tests;

public sealed class ContentInfoTests
{
    [Fact]
    public void Create_TrimsTheText_AndDropsABlankOriginalTitle()
    {
        var info = ContentInfo.Create("  Night Train ", "   ", " Journey ", 2024, AgeRating.Age7).Value;

        Assert.Equal("Night Train", info.Title);
        Assert.Null(info.OriginalTitle);
        Assert.Equal("Journey", info.Synopsis);
    }

    [Theory]
    [InlineData(null, "Text", 2024, "Content.TitleRequired")]
    [InlineData("  ", "Text", 2024, "Content.TitleRequired")]
    [InlineData("Title", null, 2024, "Content.SynopsisRequired")]
    [InlineData("Title", "Text", ContentInfo.MinReleaseYear - 1, "Content.ReleaseYearOutOfRange")]
    [InlineData("Title", "Text", ContentInfo.MaxReleaseYear + 1, "Content.ReleaseYearOutOfRange")]
    public void Create_RejectsInvalidValues(string? title, string? synopsis, int year, string code)
        => Assert.Equal(code, ContentInfo.Create(title, null, synopsis, year, AgeRating.All).Error.Code);

    [Theory]
    [InlineData(ContentInfo.MinReleaseYear)]
    [InlineData(ContentInfo.MaxReleaseYear)]
    public void Create_AcceptsTheYearLimits(int year)
        => Assert.True(ContentInfo.Create("Title", null, "Text", year, AgeRating.All).IsSuccess);

    [Fact]
    public void Create_RejectsTooLongText()
    {
        var longTitle = new string('a', ContentInfo.TitleMaxLength + 1);
        var longSynopsis = new string('a', ContentInfo.SynopsisMaxLength + 1);

        Assert.Equal(ContentErrors.TitleTooLong, Failure(longTitle, null, "Text"));
        Assert.Equal(ContentErrors.OriginalTitleTooLong, Failure("Title", longTitle, "Text"));
        Assert.Equal(ContentErrors.SynopsisTooLong, Failure("Title", null, longSynopsis));
    }

    [Fact]
    public void Create_RejectsAnUnknownAgeRating()
        => Assert.Equal(ContentErrors.AgeRatingInvalid, ContentInfo.Create("Title", null, "Text", 2024, (AgeRating)99).Error);

    private static Error Failure(string title, string? original, string synopsis)
        => ContentInfo.Create(title, original, synopsis, 2024, AgeRating.All).Error;
}
