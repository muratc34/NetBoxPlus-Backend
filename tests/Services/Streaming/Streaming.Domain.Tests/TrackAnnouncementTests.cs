using Streaming.Domain.Videos;

namespace Streaming.Domain.Tests;

/// <summary>What a ready video offers players is announced whenever it changes, and only then.</summary>
public sealed class TrackAnnouncementTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private static Video Ready()
    {
        var video = Video.StartUpload(ContentKind.Episode, Guid.NewGuid(), "en-GB", Now).Value;
        video.ConfirmUpload(1000, Now);
        video.MarkReady(600, [new Rendition(720, 1280, 2800000, "720p/index.m3u8")], 128000);
        return video;
    }

    private static VideoTracksChangedDomainEvent Announced(Video video)
    {
        var announced = Assert.IsType<VideoTracksChangedDomainEvent>(Assert.Single(video.DomainEvents));
        Assert.Equal((video.Id, ContentKind.Episode, video.ContentId, video.Revision), (announced.VideoId, announced.ContentKind, announced.ContentId, announced.Revision));
        video.ClearDomainEvents();
        return announced;
    }

    [Fact]
    public void AReadyVideo_OffersItsOriginalSound()
    {
        var video = Ready();

        var announced = Assert.IsType<VideoTracksChangedDomainEvent>(video.DomainEvents.Last());
        Assert.Equal(["en-GB"], announced.AudioLanguages);
        Assert.Empty(announced.SubtitleLanguages);
        Assert.Equal(3, announced.Revision);
    }

    [Fact]
    public void EveryChangeOfWhatIsOffered_IsAnnounced_WithAHigherRevision()
    {
        var video = Ready();
        video.ClearDomainEvents();

        var dub = video.StartAudioTrack("de-DE", Now).Value;
        video.ConfirmAudioTrack(dub.Id, 1000, Now);
        Assert.Empty(video.DomainEvents);
        video.MarkAudioTrackReady(dub.Id, 600, 128000);
        Assert.Equal(["en-GB", "de-DE"], Announced(video).AudioLanguages);

        var subtitle = video.StartSubtitle("tr-TR", Now).Value;
        Assert.Empty(video.DomainEvents);
        video.AcceptSubtitle(subtitle.Id, Now);
        var withSubtitles = Announced(video);
        Assert.Equal(["tr-TR"], withSubtitles.SubtitleLanguages);
        Assert.Equal(5, withSubtitles.Revision);

        video.RemoveSubtitle("tr-TR");
        Assert.Empty(Announced(video).SubtitleLanguages);
        video.RemoveAudioTrack("de-DE");
        Assert.Equal(["en-GB"], Announced(video).AudioLanguages);
        Assert.Equal(7, video.Revision);
    }

    [Fact]
    public void ADubNotReady_IsNotAmongTheLanguagesAnnounced()
    {
        var video = Ready();
        var waiting = video.StartAudioTrack("de-DE", Now).Value;
        video.ConfirmAudioTrack(waiting.Id, 1000, Now);
        video.StartAudioTrack("fr-FR", Now);
        video.ClearDomainEvents();

        video.AcceptSubtitle(video.StartSubtitle("tr-TR", Now).Value.Id, Now);

        Assert.Equal(["en-GB"], Announced(video).AudioLanguages);
    }

    [Fact]
    public void WhatWasNeverOffered_IsNotAnnouncedWhenRemoved_NorAFailedDub()
    {
        var video = Ready();
        video.ClearDomainEvents();
        var failed = video.StartAudioTrack("de-DE", Now).Value;
        video.ConfirmAudioTrack(failed.Id, 1000, Now);
        video.MarkAudioTrackReady(failed.Id, 300, 128000);
        video.StartAudioTrack("fr-FR", Now);
        video.StartSubtitle("tr-TR", Now);

        video.RemoveAudioTrack("de-DE");
        video.RemoveAudioTrack("fr-FR");
        video.RemoveSubtitle("tr-TR");

        Assert.Empty(video.DomainEvents);
    }
}
