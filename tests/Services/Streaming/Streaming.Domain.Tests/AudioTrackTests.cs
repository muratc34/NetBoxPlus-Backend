using Streaming.Domain.Videos;

namespace Streaming.Domain.Tests;

public sealed class AudioTrackTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly Rendition[] Renditions = [new(720, 1280, 2800000, "720p/index.m3u8")];

    private static Video Ready(int? originalAudio = 128000, string language = "tr-TR")
    {
        var video = Video.StartUpload(ContentKind.Movie, Guid.NewGuid(), language, Now).Value;
        video.ConfirmUpload(1000, Now);
        video.MarkReady(600, Renditions, originalAudio);
        video.ClearDomainEvents();
        return video;
    }

    private static AudioTrack Queued(Video video, string language)
    {
        var track = video.StartAudioTrack(language, Now).Value;
        Assert.True(video.ConfirmAudioTrack(track.Id, 1000, Now).IsSuccess);
        return track;
    }

    [Fact]
    public void ANewVideo_KeepsItsSoundApart_InTheLanguageItWasGiven()
    {
        var video = Video.StartUpload(ContentKind.Movie, Guid.NewGuid(), "en-GB", Now).Value;

        Assert.Equal((AudioLayout.Separate, "en-GB"), (video.AudioLayout, video.OriginalAudioLanguage));
        Assert.Equal(VideoErrors.LanguageUnsupported, Video.StartUpload(ContentKind.Movie, Guid.NewGuid(), "tr", Now).Error);
        Assert.Equal(VideoErrors.LanguageUnsupported, Video.StartUpload(ContentKind.Movie, Guid.NewGuid(), "tr-US", Now).Error);
    }

    [Fact]
    public void AConvertedVideo_GetsItsOriginalSound_AsAReadyTrack()
    {
        var video = Ready(language: "de-DE");

        var original = Assert.Single(video.AudioTracks);
        Assert.Equal(("de-DE", true, TrackStatus.Ready, 128000, "audio/original/index.m3u8"), (original.Language, original.IsOriginal, original.Status, original.Bandwidth, original.Playlist));
    }

    [Fact]
    public void ASilentVideo_HasNoTrack() => Assert.Empty(Ready(originalAudio: null).AudioTracks);

    [Fact]
    public void ADub_IsUploaded_Converted_AndReady()
    {
        var video = Ready();

        var started = video.StartAudioTrack("en-US", Now);
        var track = started.Value;
        Assert.Equal((TrackStatus.AwaitingUpload, false, Now + Video.UploadLifetime), (track.Status, track.IsOriginal, track.UploadExpiresAt));

        Assert.True(video.ConfirmAudioTrack(track.Id, 1000, Now).IsSuccess);
        Assert.Equal(TrackStatus.Queued, track.Status);

        Assert.True(video.MarkAudioTrackReady(track.Id, 601.5, 128000).IsSuccess);
        Assert.Equal((TrackStatus.Ready, 128000, $"audio/{track.Id:N}/index.m3u8"), (track.Status, track.Bandwidth, track.Playlist));
    }

    [Fact]
    public void ADubOfAnotherLength_Fails_SinceItWouldNotStayInSync()
    {
        var video = Ready();
        var track = Queued(video, "en-US");

        Assert.True(video.MarkAudioTrackReady(track.Id, 602.5, 128000).IsSuccess);

        Assert.Equal((TrackStatus.Failed, "The audio lasts 602.5 s, the video 600 s.", null), (track.Status, track.FailureReason, track.Playlist));
    }

    [Fact]
    public void ADub_NeedsAConvertedVideo_AndALanguageItDoesNotHaveYet()
    {
        var queued = Video.StartUpload(ContentKind.Movie, Guid.NewGuid(), "tr-TR", Now).Value;
        Assert.Equal(VideoErrors.NotReady, queued.StartAudioTrack("en-US", Now).Error);

        var video = Ready();
        Assert.Equal(VideoErrors.LanguageUnsupported, video.StartAudioTrack("xx-XX", Now).Error);
        Assert.Equal(VideoErrors.AudioTrackExists, video.StartAudioTrack("tr-TR", Now).Error);
        Assert.True(video.StartAudioTrack("en-US", Now).IsSuccess);
        Assert.Equal(VideoErrors.AudioTrackExists, video.StartAudioTrack("en-US", Now).Error);
    }

    [Fact]
    public void AFailedDub_OrOneNeverUploaded_MakesRoomForANewOne()
    {
        var video = Ready();
        var failed = Queued(video, "en-US");
        video.MarkAudioTrackFailed(failed.Id, "No sound.");
        var abandoned = video.StartAudioTrack("de-DE", Now).Value;

        Assert.Equal(VideoErrors.AudioTrackExists, video.StartAudioTrack("de-DE", Now + Video.UploadLifetime - TimeSpan.FromSeconds(1)).Error);
        var again = video.StartAudioTrack("en-US", Now).Value;
        var later = video.StartAudioTrack("de-DE", Now + Video.UploadLifetime).Value;

        Assert.DoesNotContain(video.AudioTracks, track => track.Id == failed.Id || track.Id == abandoned.Id);
        Assert.Contains(again, video.AudioTracks);
        Assert.Contains(later, video.AudioTracks);
    }

    [Fact]
    public void Confirming_NeedsTheTrack_TheFile_AndTime()
    {
        var video = Ready();
        var track = video.StartAudioTrack("en-US", Now).Value;

        Assert.Equal(VideoErrors.AudioTrackNotFound, video.ConfirmAudioTrack(Guid.NewGuid(), 1000, Now).Error);
        Assert.Equal(VideoErrors.NotUploaded, video.ConfirmAudioTrack(track.Id, null, Now).Error);
        Assert.Equal(VideoErrors.TooLarge, video.ConfirmAudioTrack(track.Id, Video.MaxBytes + 1, Now).Error);
        Assert.Equal(VideoErrors.UploadExpired, video.ConfirmAudioTrack(track.Id, 1000, track.UploadExpiresAt).Error);
        Assert.True(video.ConfirmAudioTrack(track.Id, 1000, Now).IsSuccess);
        Assert.Equal(VideoErrors.AlreadyConfirmed, video.ConfirmAudioTrack(track.Id, 1000, Now).Error);
    }

    [Fact]
    public void OnlyAQueuedDub_GetsAResult()
    {
        var video = Ready();
        var waiting = video.StartAudioTrack("en-US", Now).Value;

        Assert.Equal(VideoErrors.NotQueued, video.MarkAudioTrackReady(waiting.Id, 600, 128000).Error);
        Assert.Equal(VideoErrors.NotQueued, video.MarkAudioTrackFailed(waiting.Id, "No.").Error);
        Assert.Equal(VideoErrors.AudioTrackNotFound, video.MarkAudioTrackReady(Guid.NewGuid(), 600, 128000).Error);
        Assert.Equal(VideoErrors.AudioTrackNotFound, video.MarkAudioTrackFailed(Guid.NewGuid(), "No.").Error);
    }

    [Fact]
    public void ADub_CanBeRemoved_ButNotTheOriginal()
    {
        var video = Ready();
        Queued(video, "en-US");

        Assert.Equal(VideoErrors.OriginalAudioKept, video.RemoveAudioTrack("tr-TR").Error);
        Assert.Equal(VideoErrors.AudioTrackNotFound, video.RemoveAudioTrack("de-DE").Error);
        Assert.True(video.RemoveAudioTrack("en-US").IsSuccess);
        Assert.Equal("tr-TR", Assert.Single(video.AudioTracks).Language);
    }
}
