using Catalog.Domain.Content;
using Catalog.Domain.Movies;
using Catalog.Domain.Shows;

namespace Catalog.Domain.Tests;

internal static class TestContent
{
    public static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    public static ContentInfo Info(string? originalTitle = null)
        => ContentInfo.Create(originalTitle, 2024, AgeRating.Age13).Value;

    public static IReadOnlyDictionary<string, ContentText> Texts(string title = "Gece Treni", string? english = null)
    {
        var texts = new Dictionary<string, ContentText> { [Languages.Turkish] = ContentText.Create(title, "Uzun bir yolculuk.").Value };
        if (english is not null)
        {
            texts[Languages.English] = ContentText.Create(english, "A long journey.").Value;
        }

        return texts;
    }

    public static Movie Movie() => Catalog.Domain.Movies.Movie.Create(Info(), Texts(), 120).Value;

    public static EpisodeDetails Details(int number = 1)
        => EpisodeDetails.Create(number, Texts($"Episode {number}"), 45).Value;

    public static (Series Series, Season Season) SeriesWithSeason()
    {
        var series = Series.Create(Info(), Texts()).Value;
        return (series, series.AddSeason(1, null).Value);
    }
}
