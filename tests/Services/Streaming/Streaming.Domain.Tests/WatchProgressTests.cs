using Streaming.Domain.Access;
using Streaming.Domain.Playback;

namespace Streaming.Domain.Tests;

public sealed class WatchProgressTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 21, 0, 0, TimeSpan.Zero);

    private static readonly Guid User = Guid.CreateVersion7();

    [Theory]
    [InlineData(7200, 6839, false)] // 6 min 1 s left of 2 h: the last 5% is 6 min
    [InlineData(7200, 6840, true)]
    [InlineData(1500, 1379, false)] // 25 min: 2 minutes count, more than its 5%
    [InlineData(1500, 1380, true)]
    [InlineData(600, 539, false)] // 10 min: 2 minutes would be 20%, so only the last 10%
    [InlineData(600, 540, true)]
    [InlineData(8, 7.2, true)]
    [InlineData(8, 1, false)]
    [InlineData(0, 0, false)]
    public void WatchedToTheEnd_MeansTheLastPart(double duration, double position, bool finished)
        => Assert.Equal(finished, WatchProgress.IsFinished(position, duration));

    [Fact]
    public void APosition_IsKeptWithinTheVideo_AndResumesUnlessFinished()
    {
        var session = Session();

        var beyond = WatchProgress.Record(session, 9000, 7200, Now);
        var middle = WatchProgress.Record(session, 1800, 7200, Now);

        Assert.Equal((7200d, true, 0d), (beyond.PositionSeconds, beyond.Completed, beyond.ResumeFromSeconds));
        Assert.Equal((1800d, false, 1800d), (middle.PositionSeconds, middle.Completed, middle.ResumeFromSeconds));
        Assert.Equal(
            (session.ProfileId, session.ContentId, session.UserId, session.TitleId, Now),
            (middle.ProfileId, middle.ContentId, middle.UserId, middle.TitleId, middle.UpdatedAt));
    }

    [Fact]
    public void ContinueWatching_ShowsUnfinishedMovies_AndTheNextEpisode_MostRecentFirst()
    {
        var movie = Title(TitleKind.Movie, AgeRating.All, (Guid.CreateVersion7(), 0));
        var series = Title(TitleKind.Series, AgeRating.All, (Guid.CreateVersion7(), 100_001), (Guid.CreateVersion7(), 100_002), (Guid.CreateVersion7(), 200_001));
        var episodes = series.Items.OrderBy(item => item.Sequence).ToList();

        var entries = Pick(
            [
                Progress(series, episodes[0].ContentId, 3000, 3000, Now),
                Progress(movie, movie.Id, 600, 7200, Now.AddMinutes(-5)),
                Progress(series, episodes[1].ContentId, 100, 3000, Now.AddDays(-1)),
            ],
            [movie, series]);

        Assert.Equal([series.Id, movie.Id], entries.Select(entry => entry.TitleId));
        Assert.Equal((episodes[1].ContentId, 0d), (entries[0].ContentId, entries[0].PositionSeconds));
        Assert.Equal((movie.Id, 600d), (entries[1].ContentId, entries[1].PositionSeconds));
    }

    [Fact]
    public void ContinueWatching_KeepsAnUnfinishedEpisode_AndDropsFinishedTitles()
    {
        var finishedMovie = Title(TitleKind.Movie, AgeRating.All, (Guid.CreateVersion7(), 0));
        var finishedSeries = Title(TitleKind.Series, AgeRating.All, (Guid.CreateVersion7(), 100_001));
        var running = Title(TitleKind.Series, AgeRating.All, (Guid.CreateVersion7(), 100_001), (Guid.CreateVersion7(), 100_002));
        var first = running.Items.MinBy(item => item.Sequence)!;

        var entries = Pick(
            [
                Progress(finishedMovie, finishedMovie.Id, 7200, 7200, Now),
                Progress(finishedSeries, finishedSeries.Items.Single().ContentId, 3000, 3000, Now),
                Progress(running, first.ContentId, 120, 3000, Now),
            ],
            [finishedMovie, finishedSeries, running]);

        var entry = Assert.Single(entries);
        Assert.Equal((running.Id, first.ContentId, 120d), (entry.TitleId, entry.ContentId, entry.PositionSeconds));
    }

    [Fact]
    public void ContinueWatching_LeavesOutWhatCannotOrMayNotBeWatched()
    {
        var adult = Title(TitleKind.Movie, AgeRating.Age18, (Guid.CreateVersion7(), 0));
        var takenDown = Title(TitleKind.Movie, AgeRating.All);
        var gone = Title(TitleKind.Movie, AgeRating.All, (Guid.CreateVersion7(), 0));
        var hiddenEpisode = Title(TitleKind.Series, AgeRating.All, (Guid.CreateVersion7(), 100_002));

        var entries = Pick(
            [
                Progress(adult, adult.Id, 60, 7200, Now),
                Progress(takenDown, takenDown.Id, 60, 7200, Now),
                Progress(gone, gone.Id, 60, 7200, Now),
                Progress(hiddenEpisode, Guid.CreateVersion7(), 60, 3000, Now),
            ],
            [adult, takenDown, hiddenEpisode],
            AgeRating.Age13);

        Assert.Empty(entries);
    }

    [Fact]
    public void ContinueWatching_TakesOnlyTheLatestOfATitle_AndAtMostTwenty()
    {
        var titles = Enumerable.Range(0, 25).Select(_ => Title(TitleKind.Movie, AgeRating.All, (Guid.CreateVersion7(), 0))).ToList();
        var progress = titles.Select((title, index) => Progress(title, title.Id, 60 + index, 7200, Now.AddMinutes(-index))).ToList();
        progress.Insert(1, Progress(titles[0], titles[0].Id, 5, 7200, Now.AddDays(-2)));

        var entries = Pick(progress, titles);

        Assert.Equal(ContinueWatching.MaxEntries, entries.Count);
        Assert.Equal(60d, entries[0].PositionSeconds);
        Assert.Equal(titles.Take(20).Select(title => title.Id), entries.Select(entry => entry.TitleId));
    }

    private static IReadOnlyList<ContinueWatchingEntry> Pick(List<WatchProgress> progress, List<PlayableTitle> titles, AgeRating profileRating = AgeRating.Age18)
    {
        var profile = ProfileCopy.Create(Guid.CreateVersion7(), User);
        profile.Apply(profileRating, 1);
        return ContinueWatching.Pick(progress, titles.ToDictionary(title => title.Id), profile);
    }

    private static PlayableTitle Title(TitleKind kind, AgeRating rating, params (Guid ContentId, int Sequence)[] items)
    {
        var title = PlayableTitle.Create(Guid.CreateVersion7(), kind);
        var contents = kind == TitleKind.Movie ? items.Select(item => (title.Id, item.Sequence)).ToArray() : items;
        title.Replace(rating, contents.Select(item => new PlayableItem(item.Item1, Guid.CreateVersion7(), item.Item2)), 1);
        return title;
    }

    private static WatchProgress Progress(PlayableTitle title, Guid contentId, double position, double duration, DateTimeOffset at)
    {
        var viewer = Viewer.Create(User);
        var session = viewer.Start(new PlaybackGrant(Guid.CreateVersion7(), contentId, title.Id, Guid.CreateVersion7(), 1080), 1, at).Value;
        return WatchProgress.Record(session, position, duration, at);
    }

    private static PlaybackSession Session()
        => Viewer.Create(User).Start(new PlaybackGrant(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1080), 1, Now).Value;
}
