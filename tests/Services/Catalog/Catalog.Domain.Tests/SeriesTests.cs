using Catalog.Domain.Content;
using Catalog.Domain.Shows;

namespace Catalog.Domain.Tests;

public sealed class SeriesTests
{
    [Fact]
    public void AddSeason_RejectsATakenNumber_AndOneOutOfRange()
    {
        var (series, _) = TestContent.SeriesWithSeason();

        Assert.Equal(SeriesErrors.SeasonNumberTaken, series.AddSeason(1, null).Error);
        Assert.Equal(SeriesErrors.SeasonNumberOutOfRange, series.AddSeason(0, null).Error);
        Assert.Equal(SeriesErrors.SeasonNumberOutOfRange, series.AddSeason(Season.MaxNumber + 1, null).Error);
        Assert.Equal(SeriesErrors.SeasonTitleTooLong, series.AddSeason(2, new Dictionary<string, string?> { ["tr"] = new string('a', Season.TitleMaxLength + 1) }).Error);
    }

    [Fact]
    public void ChangeSeason_CanKeepItsOwnNumber_ButNotTakeAnother()
    {
        var (series, first) = TestContent.SeriesWithSeason();
        series.AddSeason(2, null);

        Assert.True(series.ChangeSeason(first.Id, 1, new Dictionary<string, string?> { ["tr"] = "Beginnings" }).IsSuccess);
        Assert.Equal("Beginnings", first.TitleFor("tr"));
        Assert.Equal(SeriesErrors.SeasonNumberTaken, series.ChangeSeason(first.Id, 2, null).Error);
        Assert.Equal(SeriesErrors.SeasonNotFound, series.ChangeSeason(Guid.CreateVersion7(), 3, null).Error);
    }

    [Fact]
    public void AddEpisode_RejectsANumberTakenInTheSameSeason_ButAllowsItInAnother()
    {
        var (series, first) = TestContent.SeriesWithSeason();
        var second = series.AddSeason(2, null).Value;
        series.AddEpisode(first.Id, TestContent.Details());

        Assert.Equal(SeriesErrors.EpisodeNumberTaken, series.AddEpisode(first.Id, TestContent.Details()).Error);
        Assert.True(series.AddEpisode(second.Id, TestContent.Details()).IsSuccess);
        Assert.Equal(SeriesErrors.SeasonNotFound, series.AddEpisode(Guid.CreateVersion7(), TestContent.Details()).Error);
    }

    [Fact]
    public void ChangeEpisode_CanKeepItsOwnNumber_ButNotTakeAnother()
    {
        var (series, season) = TestContent.SeriesWithSeason();
        var first = series.AddEpisode(season.Id, TestContent.Details(1)).Value;
        series.AddEpisode(season.Id, TestContent.Details(2));

        Assert.True(series.ChangeEpisode(first.Id, TestContent.Details(1)).IsSuccess);
        Assert.Equal(SeriesErrors.EpisodeNumberTaken, series.ChangeEpisode(first.Id, TestContent.Details(2)).Error);
    }

    [Theory]
    [InlineData(0, 45, "Series.EpisodeNumberOutOfRange")]
    [InlineData(1, 0, "Content.RuntimeOutOfRange")]
    public void EpisodeDetails_RejectsInvalidValues(int number, int minutes, string code)
        => Assert.Equal(code, EpisodeDetails.Create(number, TestContent.Texts(), minutes).Error.Code);

    [Fact]
    public void EpisodeDetails_NeedTheDefaultLanguage()
        => Assert.Equal(ContentErrors.DefaultLanguageRequired, EpisodeDetails.Create(1, new Dictionary<string, ContentText>(), 45).Error);

    [Fact]
    public void SeasonTitles_AreOptional_AndFallBackToTheDefaultLanguage()
    {
        var (series, season) = TestContent.SeriesWithSeason();
        Assert.Null(season.TitleFor("en"));

        Assert.True(series.ChangeSeason(season.Id, 1, new Dictionary<string, string?> { ["tr"] = "Başlangıç", ["en"] = " " }).IsSuccess);

        Assert.Equal("Başlangıç", season.TitleFor("en"));
        Assert.Single(season.Translations);
    }

    [Fact]
    public void Publish_NeedsAPublishedEpisode()
    {
        var (series, season) = WithGenre();
        var episode = series.AddEpisode(season.Id, TestContent.Details()).Value;

        Assert.Equal(SeriesErrors.PublishedEpisodeRequired, series.Publish(TestContent.Now).Error);

        series.PublishEpisode(episode.Id, TestContent.Now);
        Assert.True(series.Publish(TestContent.Now).IsSuccess);
    }

    [Fact]
    public void Publish_StillNeedsAGenre()
    {
        var (series, season) = TestContent.SeriesWithSeason();
        var episode = series.AddEpisode(season.Id, TestContent.Details()).Value;
        series.PublishEpisode(episode.Id, TestContent.Now);

        Assert.Equal(ContentErrors.GenreRequired, series.Publish(TestContent.Now).Error);
    }

    [Fact]
    public void NewEpisode_OfAPublishedSeries_StaysHiddenUntilPublished()
    {
        var (series, season, _) = PublishedSeries();

        var added = series.AddEpisode(season.Id, TestContent.Details(2)).Value;

        Assert.False(added.IsPublished);
        Assert.True(series.IsPublished);

        series.PublishEpisode(added.Id, TestContent.Now);
        Assert.True(added.IsPublished);
        Assert.Equal(TestContent.Now, added.PublishedAt);
    }

    [Fact]
    public void PublishedSeries_CannotLoseItsLastPublishedEpisode()
    {
        var (series, _, episode) = PublishedSeries();

        Assert.Equal(SeriesErrors.PublishedEpisodeRequired, series.UnpublishEpisode(episode.Id).Error);
        Assert.True(episode.IsPublished);
    }

    [Fact]
    public void PublishedSeries_CanHideAnEpisode_WhileAnotherRemains()
    {
        var (series, season, episode) = PublishedSeries();
        var second = series.AddEpisode(season.Id, TestContent.Details(2)).Value;
        series.PublishEpisode(second.Id, TestContent.Now);

        Assert.True(series.UnpublishEpisode(episode.Id).IsSuccess);
        Assert.False(episode.IsPublished);
        Assert.Null(episode.PublishedAt);
    }

    [Fact]
    public void DraftSeries_CanHideItsOnlyEpisode()
    {
        var (series, season) = TestContent.SeriesWithSeason();
        var episode = series.AddEpisode(season.Id, TestContent.Details()).Value;
        series.PublishEpisode(episode.Id, TestContent.Now);

        Assert.True(series.UnpublishEpisode(episode.Id).IsSuccess);
    }

    [Fact]
    public void PublishedEpisode_CannotBeRemoved_NorItsSeason()
    {
        var (series, season, episode) = PublishedSeries();

        Assert.Equal(SeriesErrors.EpisodeIsPublished, series.RemoveEpisode(episode.Id).Error);
        Assert.Equal(SeriesErrors.SeasonHasPublishedEpisodes, series.RemoveSeason(season.Id).Error);
        Assert.Single(season.Episodes);
        Assert.Single(series.Seasons);
    }

    [Fact]
    public void DraftEpisode_AndEmptiedSeason_CanBeRemoved()
    {
        var (series, season) = TestContent.SeriesWithSeason();
        var episode = series.AddEpisode(season.Id, TestContent.Details()).Value;

        Assert.True(series.RemoveEpisode(episode.Id).IsSuccess);
        Assert.Empty(season.Episodes);
        Assert.True(series.RemoveSeason(season.Id).IsSuccess);
        Assert.Empty(series.Seasons);
    }

    [Fact]
    public void ChangesToSeasonsAndEpisodes_RaiseTheVersionOfTheSeries()
    {
        var (series, season) = TestContent.SeriesWithSeason();
        var before = series.Version;

        var episode = series.AddEpisode(season.Id, TestContent.Details()).Value;
        var afterAdd = series.Version;
        series.ChangeEpisode(episode.Id, TestContent.Details(2));
        var afterChange = series.Version;
        series.PublishEpisode(episode.Id, TestContent.Now);
        var afterPublish = series.Version;

        Assert.True(before < afterAdd && afterAdd < afterChange && afterChange < afterPublish);
    }

    [Fact]
    public void UnknownEpisode_IsReportedByEveryOperation()
    {
        var (series, _) = TestContent.SeriesWithSeason();
        var unknown = Guid.CreateVersion7();

        Assert.Equal(SeriesErrors.EpisodeNotFound, series.ChangeEpisode(unknown, TestContent.Details()).Error);
        Assert.Equal(SeriesErrors.EpisodeNotFound, series.RemoveEpisode(unknown).Error);
        Assert.Equal(SeriesErrors.EpisodeNotFound, series.PublishEpisode(unknown, TestContent.Now).Error);
        Assert.Equal(SeriesErrors.EpisodeNotFound, series.UnpublishEpisode(unknown).Error);
    }

    private static (Series Series, Season Season) WithGenre()
    {
        var (series, season) = TestContent.SeriesWithSeason();
        series.SetGenres([Guid.CreateVersion7()]);
        series.SetImage(ContentImageSlot.Poster, "poster.jpg");
        return (series, season);
    }

    private static (Series Series, Season Season, Episode Episode) PublishedSeries()
    {
        var (series, season) = WithGenre();
        var episode = series.AddEpisode(season.Id, TestContent.Details()).Value;
        series.PublishEpisode(episode.Id, TestContent.Now);
        series.Publish(TestContent.Now);
        return (series, season, episode);
    }
}
