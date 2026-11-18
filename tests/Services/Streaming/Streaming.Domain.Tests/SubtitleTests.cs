using Streaming.Domain.Videos;

namespace Streaming.Domain.Tests;

public sealed class SubtitleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private static Video Ready()
    {
        var video = Video.StartUpload(ContentKind.Movie, Guid.NewGuid(), MediaLanguages.Default, Now).Value;
        video.ConfirmUpload(1000, Now);
        video.MarkReady(600, [new Rendition(720, 1280, 2800000, "720p/index.m3u8")]);
        return video;
    }

    [Fact]
    public void Subtitles_AreUploaded_ThenAccepted_IntoTheirFile()
    {
        var video = Ready();

        var subtitle = video.StartSubtitle("en-US", Now).Value;
        Assert.Equal((TrackStatus.AwaitingUpload, Now + Video.UploadLifetime, null), (subtitle.Status, subtitle.UploadExpiresAt, subtitle.File));
        Assert.Same(subtitle, video.SubtitleAwaitingUpload(subtitle.Id, Now).Value);

        Assert.True(video.AcceptSubtitle(subtitle.Id, Now).IsSuccess);
        Assert.Equal((TrackStatus.Ready, $"subtitles/{subtitle.Id:N}.vtt"), (subtitle.Status, subtitle.File));
        Assert.Equal(VideoErrors.AlreadyConfirmed, video.AcceptSubtitle(subtitle.Id, Now).Error);
    }

    [Fact]
    public void Subtitles_NeedAConvertedVideo_AndALanguageItDoesNotHaveYet()
    {
        var waiting = Video.StartUpload(ContentKind.Movie, Guid.NewGuid(), MediaLanguages.Default, Now).Value;
        Assert.Equal(VideoErrors.NotReady, waiting.StartSubtitle("tr-TR", Now).Error);

        var video = Ready();
        Assert.Equal(VideoErrors.LanguageUnsupported, video.StartSubtitle("en", Now).Error);
        Assert.True(video.StartSubtitle("tr-TR", Now).IsSuccess);
        Assert.Equal(VideoErrors.SubtitleExists, video.StartSubtitle("tr-TR", Now + Video.UploadLifetime - TimeSpan.FromSeconds(1)).Error);
    }

    [Fact]
    public void AnUploadNeverFinished_MakesRoomForANewOne_AndCannotBeAcceptedAnyMore()
    {
        var video = Ready();
        var abandoned = video.StartSubtitle("tr-TR", Now).Value;
        var later = Now + Video.UploadLifetime;

        Assert.Equal(VideoErrors.UploadExpired, video.AcceptSubtitle(abandoned.Id, later).Error);
        var again = video.StartSubtitle("tr-TR", later).Value;

        Assert.Equal(again, Assert.Single(video.Subtitles));
        Assert.Equal(VideoErrors.SubtitleNotFound, video.SubtitleAwaitingUpload(abandoned.Id, later).Error);
    }

    [Fact]
    public void AcceptedSubtitles_AreNotReplaced_ButCanBeRemoved()
    {
        var video = Ready();
        var subtitle = video.StartSubtitle("tr-TR", Now).Value;
        video.AcceptSubtitle(subtitle.Id, Now);

        Assert.Equal(VideoErrors.SubtitleExists, video.StartSubtitle("tr-TR", Now + Video.UploadLifetime).Error);
        Assert.Equal(VideoErrors.SubtitleNotFound, video.RemoveSubtitle("en-US").Error);
        Assert.True(video.RemoveSubtitle("tr-TR").IsSuccess);
        Assert.Empty(video.Subtitles);
    }
}
