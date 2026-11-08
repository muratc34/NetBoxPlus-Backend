using Catalog.Domain.Content;
using Catalog.Domain.Movies;
using Catalog.Domain.Shows;

namespace Catalog.Domain.Tests;

internal static class TestContent
{
    public static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    public static ContentInfo Info(string title = "Night Train")
        => ContentInfo.Create(title, null, "A long journey.", 2024, AgeRating.Age13).Value;

    public static Movie Movie() => Catalog.Domain.Movies.Movie.Create(Info(), 120).Value;

    public static EpisodeDetails Details(int number = 1)
        => EpisodeDetails.Create(number, $"Episode {number}", "Something happens.", 45).Value;

    public static (Series Series, Season Season) SeriesWithSeason()
    {
        var series = Series.Create(Info());
        return (series, series.AddSeason(1, null).Value);
    }
}
