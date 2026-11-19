using NetBoxPlus.DemoSeeder.Content;

namespace NetBoxPlus.DemoSeeder.Tests;

/// <summary>The demo set must be one the catalog accepts and the seeder can match again on a second run.</summary>
public sealed class DemoContentTests
{
    private static readonly string[] AgeRatings = ["All", "Age7", "Age13", "Age16", "Age18"];
    private static readonly string[] Roles = ["Director", "Writer", "Actor", "Creator"];

    [Fact]
    public void EveryGenreAndPersonReferred_IsInTheSet()
    {
        var genres = DemoContent.Genres.Select(genre => genre.Turkish).ToHashSet();
        var people = DemoContent.People.Select(person => person.Name).ToHashSet();

        foreach (var (genreNames, credits) in Titles())
        {
            Assert.NotEmpty(genreNames);
            Assert.All(genreNames, name => Assert.Contains(name, genres));
            Assert.All(credits, credit => Assert.Contains(credit.Person, people));
        }
    }

    [Fact]
    public void TitlesAndNames_AreUnique_SoASecondRunFindsThem()
    {
        AssertUnique(DemoContent.Genres.Select(genre => genre.Turkish));
        AssertUnique(DemoContent.Genres.Select(genre => genre.English));
        AssertUnique(DemoContent.People.Select(person => person.Name));
        AssertUnique(DemoContent.Movies.Select(movie => movie.Turkish.Title).Concat(DemoContent.Series.Select(series => series.Turkish.Title)));
        AssertUnique(DemoContent.Movies.Select(movie => movie.OriginalTitle).Concat(DemoContent.Series.Select(series => series.OriginalTitle)));
    }

    [Fact]
    public void EveryTitle_HasBothLanguages_AndValidValues()
    {
        foreach (var movie in DemoContent.Movies)
        {
            AssertTexts(movie.Turkish, movie.English);
            Assert.Contains(movie.AgeRating, AgeRatings);
            Assert.InRange(movie.RuntimeMinutes, 1, 600);
            Assert.InRange(movie.ReleaseYear, 1900, 2100);
            Assert.Contains(movie.VideoHeight, new[] { 720, 1080 });
            AssertCredits(movie.Credits);
        }

        foreach (var series in DemoContent.Series)
        {
            AssertTexts(series.Turkish, series.English);
            Assert.Contains(series.AgeRating, AgeRatings);
            AssertCredits(series.Credits);
            AssertUnique(series.Seasons.Select(season => season.Number));
            foreach (var season in series.Seasons)
            {
                Assert.False(string.IsNullOrWhiteSpace(season.TitleTurkish));
                Assert.False(string.IsNullOrWhiteSpace(season.TitleEnglish));
                Assert.NotEmpty(season.Episodes);
                AssertUnique(season.Episodes.Select(episode => episode.Number));
                Assert.All(season.Episodes, episode =>
                {
                    AssertTexts(episode.Turkish, episode.English);
                    Assert.InRange(episode.RuntimeMinutes, 1, 600);
                });
            }
        }
    }

    [Fact]
    public void TheSet_ShowsWhatTheCatalogCanDo()
    {
        // Both video qualities, so the plans' quality limit shows; several age ratings, so the profile limit does.
        Assert.Contains(DemoContent.Movies, movie => movie.VideoHeight == 1080);
        Assert.Contains(DemoContent.Movies, movie => movie.VideoHeight == 720);
        Assert.True(DemoContent.Movies.Select(movie => movie.AgeRating).Distinct().Count() >= 4);
        Assert.Equal(18, DemoContent.VideoCount);
    }

    private static IEnumerable<(IReadOnlyList<string> Genres, IReadOnlyList<CreditSeed> Credits)> Titles()
        => DemoContent.Movies.Select(movie => (movie.Genres, movie.Credits))
            .Concat(DemoContent.Series.Select(series => (series.Genres, series.Credits)));

    private static void AssertTexts(Texts turkish, Texts english)
    {
        Assert.False(string.IsNullOrWhiteSpace(turkish.Title));
        Assert.False(string.IsNullOrWhiteSpace(turkish.Synopsis));
        Assert.False(string.IsNullOrWhiteSpace(english.Title));
        Assert.False(string.IsNullOrWhiteSpace(english.Synopsis));
    }

    private static void AssertCredits(IReadOnlyList<CreditSeed> credits)
    {
        Assert.All(credits, credit => Assert.Contains(credit.Role, Roles));
        AssertUnique(credits.Select(credit => (credit.Person, credit.Role)));
    }

    private static void AssertUnique<T>(IEnumerable<T> values)
    {
        var list = values.ToList();
        Assert.Equal(list.Count, list.Distinct().Count());
    }
}
