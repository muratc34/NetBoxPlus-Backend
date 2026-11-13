using Streaming.Domain.Access;
using Streaming.Domain.Playback;

namespace Streaming.Domain.Tests;

public sealed class PlaybackTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 20, 0, 0, TimeSpan.Zero);

    private static PlaybackGrant Grant() => new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1080);

    [Fact]
    public void Sessions_StartUpToThePlansScreens()
    {
        var viewer = Viewer.Create(Guid.CreateVersion7());

        Assert.True(viewer.Start(Grant(), 2, Now).IsSuccess);
        Assert.True(viewer.Start(Grant(), 2, Now).IsSuccess);

        Assert.Equal(PlaybackErrors.TooManyScreens, viewer.Start(Grant(), 2, Now).Error);
        Assert.Equal(2, viewer.LiveSessions(Now));
    }

    [Fact]
    public void ASessionWithoutHeartbeat_FreesItsScreen_AfterTheTimeout()
    {
        var viewer = Viewer.Create(Guid.CreateVersion7());
        var first = viewer.Start(Grant(), 1, Now).Value;

        Assert.True(first.IsLive(Now + PlaybackSession.Timeout - TimeSpan.FromSeconds(1)));
        Assert.Equal(PlaybackErrors.TooManyScreens, viewer.Start(Grant(), 1, Now + PlaybackSession.Timeout - TimeSpan.FromSeconds(1)).Error);

        Assert.False(first.IsLive(Now + PlaybackSession.Timeout));
        Assert.True(viewer.Start(Grant(), 1, Now + PlaybackSession.Timeout).IsSuccess);
    }

    [Fact]
    public void StartingASession_ChangesTheVersion_ARefusalDoesNot()
    {
        var viewer = Viewer.Create(Guid.CreateVersion7());

        viewer.Start(Grant(), 1, Now);
        var version = viewer.Version;
        viewer.Start(Grant(), 1, Now);

        Assert.Equal(1, version);
        Assert.Equal(1, viewer.Version);
    }

    [Fact]
    public void ASession_KeepsWhatItWasGranted()
    {
        var viewer = Viewer.Create(Guid.CreateVersion7());
        var grant = Grant();

        var session = viewer.Start(grant, 1, Now).Value;

        Assert.Equal(
            (viewer.Id, grant.ProfileId, grant.ContentId, grant.TitleId, grant.VideoId, 1080, Now, Now, (DateTimeOffset?)null),
            (session.UserId, session.ProfileId, session.ContentId, session.TitleId, session.VideoId, session.MaxHeight, session.StartedAt, session.LastHeartbeatAt, session.EndedAt));
    }

    [Theory]
    [InlineData(AgeRating.Age13, AgeRating.Age13, true)]
    [InlineData(AgeRating.Age13, AgeRating.Age7, true)]
    [InlineData(AgeRating.Age13, AgeRating.Age16, false)]
    [InlineData(AgeRating.All, AgeRating.Age7, false)]
    [InlineData(AgeRating.Age18, AgeRating.Age18, true)]
    public void AProfile_WatchesTitlesUpToItsAgeRating(AgeRating profileRating, AgeRating titleRating, bool allowed)
    {
        var profile = ProfileCopy.Create(Guid.CreateVersion7(), Guid.CreateVersion7());
        profile.Apply(profileRating, 1);

        Assert.Equal(allowed, profile.MayWatch(titleRating));
    }

    [Fact]
    public void ARemovedProfile_WatchesNothing()
    {
        var profile = ProfileCopy.Create(Guid.CreateVersion7(), Guid.CreateVersion7());
        profile.Apply(AgeRating.Age18, 1);
        profile.Remove(2);

        Assert.False(profile.MayWatch(AgeRating.All));
    }

    [Theory]
    [InlineData(VideoQuality.Sd, 480)]
    [InlineData(VideoQuality.Hd, 1080)]
    [InlineData(VideoQuality.Uhd, int.MaxValue)]
    public void APlansQuality_CapsTheRenditions(VideoQuality quality, int maxHeight)
        => Assert.Equal(maxHeight, VideoQualities.MaxHeight(quality));
}
