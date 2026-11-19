using Streaming.Domain.Videos;

namespace Streaming.Domain.Tests;

public sealed class TrailerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly Rendition[] Renditions = [new(720, 1280, 2928000, "720p/index.m3u8"), new(360, 640, 928000, "360p/index.m3u8")];

    private static Video Trailer(ContentKind kind = ContentKind.Series)
        => Video.StartUpload(kind, Guid.NewGuid(), MediaLanguages.Default, Now, VideoPurpose.Trailer).Value;

    private static Video ReadyTrailer()
    {
        var video = Trailer();
        video.ConfirmUpload(1000, Now);
        video.MarkReady(90, Renditions, 128_000);
        return video;
    }

    [Theory]
    [InlineData(ContentKind.Movie)]
    [InlineData(ContentKind.Series)]
    public void ATrailer_BelongsToAMovieOrASeries(ContentKind kind)
    {
        var video = Trailer(kind);

        Assert.Equal((VideoPurpose.Trailer, kind), (video.Purpose, video.ContentKind));
    }

    [Fact]
    public void AnEpisode_HasNoTrailer_AndASeriesNoVideoOfItsOwn()
    {
        Assert.Equal(VideoErrors.ContentKindInvalid, Video.StartUpload(ContentKind.Episode, Guid.NewGuid(), MediaLanguages.Default, Now, VideoPurpose.Trailer).Error);
        Assert.Equal(VideoErrors.ContentKindInvalid, Video.StartUpload(ContentKind.Series, Guid.NewGuid(), MediaLanguages.Default, Now).Error);
        Assert.Equal(VideoErrors.PurposeInvalid, Video.StartUpload(ContentKind.Movie, Guid.NewGuid(), MediaLanguages.Default, Now, (VideoPurpose)7).Error);
    }

    [Fact]
    public void AVideo_IsAFeature_UnlessSaidOtherwise()
        => Assert.Equal(VideoPurpose.Feature, Video.StartUpload(ContentKind.Movie, Guid.NewGuid(), MediaLanguages.Default, Now).Value.Purpose);

    [Fact]
    public void ATrailer_IsNeverAnnounced_SoTheCatalogCannotTakeItForTheTitlesVideo()
    {
        var ready = Trailer();
        ready.ConfirmUpload(1000, Now);
        ready.MarkReady(90, Renditions, 128_000);
        var failed = Trailer();
        failed.ConfirmUpload(1000, Now);
        failed.MarkFailed("Broken file.");

        Assert.Equal((VideoStatus.Ready, VideoStatus.Failed), (ready.Status, failed.Status));
        Assert.Empty(ready.DomainEvents);
        Assert.Empty(failed.DomainEvents);
        Assert.True(ready.Revision > 0);
    }

    [Fact]
    public void ATrailer_KeepsItsOriginalSound()
    {
        var trailer = ReadyTrailer();

        var sound = Assert.Single(trailer.AudioTracks);
        Assert.True(sound.IsOriginal);
        Assert.Equal(TrackStatus.Ready, sound.Status);
    }

    [Fact]
    public void ATrailer_TakesNoDubsOrSubtitles()
    {
        var trailer = ReadyTrailer();

        Assert.Equal(VideoErrors.TrailerHasNoTracks, trailer.StartAudioTrack("en-US", Now).Error);
        Assert.Equal(VideoErrors.TrailerHasNoTracks, trailer.StartSubtitle("en-US", Now).Error);
    }

    [Fact]
    public void ATrailerLongerThanFiveMinutes_Fails_WithoutAnnouncingIt()
    {
        var trailer = Trailer();
        trailer.ConfirmUpload(1000, Now);

        Assert.True(trailer.MarkReady(300.5, Renditions, 128_000).IsSuccess);

        Assert.Equal(VideoStatus.Failed, trailer.Status);
        Assert.Contains("300", trailer.FailureReason, StringComparison.Ordinal);
        Assert.Empty(trailer.Renditions);
        Assert.Empty(trailer.DomainEvents);
    }

    [Fact]
    public void ATrailerOfExactlyFiveMinutes_IsReady()
    {
        var trailer = Trailer();
        trailer.ConfirmUpload(1000, Now);

        trailer.MarkReady(300, Renditions, 128_000);

        Assert.Equal(VideoStatus.Ready, trailer.Status);
    }

    [Fact]
    public void AFeature_OfAnyLength_IsReady()
    {
        var movie = Video.StartUpload(ContentKind.Movie, Guid.NewGuid(), MediaLanguages.Default, Now).Value;
        movie.ConfirmUpload(1000, Now);

        movie.MarkReady(3 * 60 * 60, Renditions, 128_000);

        Assert.Equal(VideoStatus.Ready, movie.Status);
        Assert.Contains(movie.DomainEvents, domainEvent => domainEvent is VideoReadyDomainEvent);
    }

    [Fact]
    public void OnlyATrailer_IsLimitedInQualityAndLength()
    {
        Assert.Equal((720, TimeSpan.FromMinutes(5)), (Video.MaxRenditionHeightOf(VideoPurpose.Trailer), Video.MaxDurationOf(VideoPurpose.Trailer)));
        Assert.Equal((null, null), (Video.MaxRenditionHeightOf(VideoPurpose.Feature), Video.MaxDurationOf(VideoPurpose.Feature)));
    }
}
