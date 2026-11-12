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

    /// <summary>A movie whose video is ready: publishing rules other than the video's are what tests look at.</summary>
    public static Movie Movie()
    {
        var movie = Catalog.Domain.Movies.Movie.Create(Info(), Texts(), 120).Value;
        movie.ApplyVideo(Guid.CreateVersion7(), VideoState.Ready, 2);
        return movie;
    }

    /// <summary>Gives the episode a ready video and hands the series back, for a following call.</summary>
    public static Series Playable(Series series, Guid episodeId)
    {
        series.ApplyEpisodeVideo(episodeId, Guid.CreateVersion7(), VideoState.Ready, 2);
        return series;
    }

    public static EpisodeDetails Details(int number = 1)
        => EpisodeDetails.Create(number, Texts($"Episode {number}"), 45).Value;

    public static (Series Series, Season Season) SeriesWithSeason()
    {
        var series = Series.Create(Info(), Texts()).Value;
        return (series, series.AddSeason(1, null).Value);
    }
}
