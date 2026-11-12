using Streaming.Domain.Videos;

namespace Streaming.Domain.Tests;

public sealed class VideoTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly Rendition[] Renditions = [new(720, 1280, 2928000, "720p/index.m3u8"), new(360, 640, 928000, "360p/index.m3u8")];

    private static Video NewVideo() => Video.StartUpload(ContentKind.Movie, Guid.NewGuid(), Now).Value;

    private static Video Queued()
    {
        var video = NewVideo();
        video.ConfirmUpload(1000, Now);
        video.ClearDomainEvents();
        return video;
    }

    [Fact]
    public void StartUpload_WaitsForTheFile_ForSixHours()
    {
        var video = NewVideo();

        Assert.Equal((VideoStatus.AwaitingUpload, Now.AddHours(6), 0), (video.Status, video.UploadExpiresAt, video.Revision));
        Assert.Empty(video.DomainEvents);
    }

    [Fact]
    public void StartUpload_NeedsAContent()
    {
        Assert.Equal(VideoErrors.ContentRequired, Video.StartUpload(ContentKind.Episode, Guid.Empty, Now).Error);
        Assert.Equal(VideoErrors.ContentKindInvalid, Video.StartUpload((ContentKind)9, Guid.NewGuid(), Now).Error);
    }

    [Fact]
    public void ConfirmUpload_QueuesTheVideo_AndAnnouncesIt()
    {
        var video = NewVideo();

        Assert.True(video.ConfirmUpload(1000, Now.AddHours(1)).IsSuccess);

        Assert.Equal((VideoStatus.Queued, 1000L, 1), (video.Status, video.SizeBytes, video.Revision));
        var queued = Assert.IsType<VideoQueuedDomainEvent>(Assert.Single(video.DomainEvents));
        Assert.Equal((video.Id, ContentKind.Movie, video.ContentId, 1), (queued.VideoId, queued.ContentKind, queued.ContentId, queued.Revision));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    public void ConfirmUpload_NeedsTheFile(long? size) => Assert.Equal(VideoErrors.NotUploaded, NewVideo().ConfirmUpload(size, Now).Error);

    [Fact]
    public void ConfirmUpload_RefusesTooLargeFiles() => Assert.Equal(VideoErrors.TooLarge, NewVideo().ConfirmUpload(Video.MaxBytes + 1, Now).Error);

    [Fact]
    public void ConfirmUpload_AcceptsTheLargestAllowedFile() => Assert.True(NewVideo().ConfirmUpload(Video.MaxBytes, Now).IsSuccess);

    [Fact]
    public void ConfirmUpload_RefusesAnExpiredUpload() => Assert.Equal(VideoErrors.UploadExpired, NewVideo().ConfirmUpload(1000, Now.AddHours(6)).Error);

    [Fact]
    public void ConfirmUpload_Once() => Assert.Equal(VideoErrors.AlreadyConfirmed, Queued().ConfirmUpload(1000, Now).Error);

    [Fact]
    public void MarkReady_KeepsTheRenditions_LowestFirst()
    {
        var video = Queued();

        Assert.True(video.MarkReady(12.5, Renditions).IsSuccess);

        Assert.Equal((VideoStatus.Ready, 12.5, 2), (video.Status, video.DurationSeconds, video.Revision));
        Assert.Equal([360, 720], video.Renditions.Select(rendition => rendition.Height));
        Assert.Equal(2, Assert.IsType<VideoReadyDomainEvent>(Assert.Single(video.DomainEvents)).Revision);
    }

    [Fact]
    public void MarkReady_NeedsSomethingConverted()
    {
        Assert.Equal(VideoErrors.NothingConverted, Queued().MarkReady(10, []).Error);
        Assert.Equal(VideoErrors.NothingConverted, Queued().MarkReady(0, Renditions).Error);
    }

    [Fact]
    public void MarkFailed_KeepsAShortReason()
    {
        var video = Queued();

        Assert.True(video.MarkFailed(new string('x', Video.FailureReasonMaxLength + 50)).IsSuccess);

        Assert.Equal(VideoStatus.Failed, video.Status);
        Assert.Equal(Video.FailureReasonMaxLength, video.FailureReason!.Length);
        Assert.IsType<VideoFailedDomainEvent>(Assert.Single(video.DomainEvents));
    }

    [Fact]
    public void Results_AreOnlyForQueuedVideos()
    {
        var waiting = NewVideo();
        var ready = Queued();
        ready.MarkReady(10, Renditions);

        Assert.Equal(VideoErrors.NotQueued, waiting.MarkReady(10, Renditions).Error);
        Assert.Equal(VideoErrors.NotQueued, ready.MarkFailed("late").Error);
        Assert.Equal(VideoErrors.NotQueued, ready.MarkReady(10, Renditions).Error);
        Assert.Equal(VideoStatus.Ready, ready.Status);
    }

    [Fact]
    public void Expire_OnlyAnUnfinishedUpload_PastItsTime()
    {
        var early = NewVideo();
        var late = NewVideo();
        var queued = Queued();

        early.Expire(Now.AddHours(5));
        late.Expire(Now.AddHours(6));
        queued.Expire(Now.AddDays(1));

        Assert.Equal((VideoStatus.AwaitingUpload, VideoStatus.Expired, VideoStatus.Queued), (early.Status, late.Status, queued.Status));
        Assert.Empty(late.DomainEvents);
    }
}
