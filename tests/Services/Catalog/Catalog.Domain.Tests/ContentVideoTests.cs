using Catalog.Domain.Content;
using Catalog.Domain.Movies;

namespace Catalog.Domain.Tests;

/// <remarks>Ids carry explicit times: version 7 Guids made in the same millisecond are in no particular order.</remarks>
public sealed class ContentVideoTests
{
    private static Movie MovieWithoutVideo() => Movie.Create(TestContent.Info(), TestContent.Texts(), 120).Value;

    private static Movie ReadyToPublishButTheVideo()
    {
        var movie = MovieWithoutVideo();
        movie.SetGenres([Guid.CreateVersion7()]);
        movie.SetImage(ContentImageSlot.Poster, "poster.jpg");
        return movie;
    }

    [Fact]
    public void AMovieWithoutAReadyVideo_CannotBePublished()
    {
        var movie = ReadyToPublishButTheVideo();
        movie.ApplyVideo(Guid.CreateVersion7(), VideoState.Processing, 1);

        Assert.Equal(ContentErrors.VideoRequired, movie.Publish(TestContent.Now).Error);
    }

    [Fact]
    public void AReadyVideo_AllowsPublishing_WithoutChangingTheVersion()
    {
        var movie = ReadyToPublishButTheVideo();
        var version = movie.Version;

        movie.ApplyVideo(Guid.CreateVersion7(), VideoState.Ready, 2);

        Assert.Equal(version, movie.Version);
        Assert.True(movie.Publish(TestContent.Now).IsSuccess);
    }

    [Fact]
    public void ANewUpload_KeepsTheReadyVideoPlayable_WhileItIsProcessed_OrFails()
    {
        var movie = MovieWithoutVideo();
        var first = Guid.CreateVersion7(TestContent.Now);
        var second = Guid.CreateVersion7(TestContent.Now.AddSeconds(1));
        movie.ApplyVideo(first, VideoState.Ready, 2);

        movie.ApplyVideo(second, VideoState.Processing, 1);
        Assert.Equal((second, VideoState.Processing, first), (movie.Video.LatestVideoId, movie.Video.LatestState, movie.Video.PlayableVideoId));

        movie.ApplyVideo(second, VideoState.Failed, 2);
        Assert.Equal((VideoState.Failed, first), (movie.Video.LatestState, movie.Video.PlayableVideoId));
    }

    [Fact]
    public void ANewerReadyVideo_TakesOver()
    {
        var movie = MovieWithoutVideo();
        var first = Guid.CreateVersion7(TestContent.Now);
        var second = Guid.CreateVersion7(TestContent.Now.AddSeconds(1));
        movie.ApplyVideo(first, VideoState.Ready, 2);

        movie.ApplyVideo(second, VideoState.Ready, 2);

        Assert.Equal((second, VideoState.Ready), (movie.Video.PlayableVideoId, movie.Video.LatestState));
    }

    [Fact]
    public void LateMessages_ChangeNothing()
    {
        var movie = MovieWithoutVideo();
        var older = Guid.CreateVersion7(TestContent.Now);
        var newer = Guid.CreateVersion7(TestContent.Now.AddSeconds(1));
        movie.ApplyVideo(newer, VideoState.Ready, 2);

        movie.ApplyVideo(newer, VideoState.Processing, 1);
        Assert.Equal(VideoState.Ready, movie.Video.LatestState);

        movie.ApplyVideo(older, VideoState.Ready, 2);
        Assert.Equal((newer, newer), (movie.Video.LatestVideoId, movie.Video.PlayableVideoId));

        movie.ApplyVideo(older, VideoState.Failed, 3);
        Assert.Equal((newer, VideoState.Ready), (movie.Video.LatestVideoId, movie.Video.LatestState));
    }

    [Fact]
    public void AnEpisodeWithoutAReadyVideo_CannotBePublished()
    {
        var (series, season) = TestContent.SeriesWithSeason();
        var episode = series.AddEpisode(season.Id, TestContent.Details()).Value;

        Assert.Equal(ContentErrors.VideoRequired, series.PublishEpisode(episode.Id, TestContent.Now).Error);

        series.ApplyEpisodeVideo(episode.Id, Guid.CreateVersion7(), VideoState.Ready, 2);
        Assert.True(series.PublishEpisode(episode.Id, TestContent.Now).IsSuccess);
        Assert.Equal(Catalog.Domain.Shows.SeriesErrors.EpisodeNotFound, series.ApplyEpisodeVideo(Guid.CreateVersion7(), Guid.CreateVersion7(), VideoState.Ready, 1).Error);
    }
}
