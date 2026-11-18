using NetBoxPlus.DemoSeeder.Gateway;
using NetBoxPlus.DemoSeeder.Seeding;

namespace NetBoxPlus.DemoSeeder.Tests;

public sealed class SeedDecisionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NoVideo_IsUploaded()
        => Assert.IsType<VideoStep.Upload>(SeedDecisions.ForVideo([]));

    [Fact]
    public void TheLatestVideo_Decides()
    {
        var ready = Video("Ready", minutesAgo: 1);
        var queued = Video("Queued", minutesAgo: 1);
        var failed = Video("Failed", minutesAgo: 1);

        Assert.Equal(ready.Id, Assert.IsType<VideoStep.Ready>(SeedDecisions.ForVideo([Video("Failed", 10), ready])).VideoId);
        Assert.Equal(queued.Id, Assert.IsType<VideoStep.Wait>(SeedDecisions.ForVideo([queued, Video("Ready", 10)])).VideoId);
        Assert.IsType<VideoStep.Upload>(SeedDecisions.ForVideo([Video("Ready", 10), failed]));
    }

    [Theory]
    [InlineData("AwaitingUpload")]
    [InlineData("Failed")]
    [InlineData("Expired")]
    public void AnUnusableLatestVideo_IsUploadedAgain(string status)
        => Assert.IsType<VideoStep.Upload>(SeedDecisions.ForVideo([Video(status, 0)]));

    [Fact]
    public void OnlyWhatIsNotPublished_IsPublished()
    {
        Assert.True(SeedDecisions.NeedsPublishing("Draft"));
        Assert.False(SeedDecisions.NeedsPublishing("Published"));
    }

    [Fact]
    public void OnlyAMissingImage_IsUploaded()
    {
        Assert.True(SeedDecisions.NeedsImage(null));
        Assert.True(SeedDecisions.NeedsImage(""));
        Assert.False(SeedDecisions.NeedsImage("http://127.0.0.1:10000/devstoreaccount1/images/a.png"));
    }

    [Theory]
    [InlineData("sk_test_abc", true)]
    [InlineData("rk_test_abc", true)]
    [InlineData("sk_live_abc", false)]
    [InlineData("rk_live_abc", false)]
    [InlineData("abc", false)]
    public void OnlyATestModeStripeKey_IsUsed(string key, bool allowed)
        => Assert.Equal(allowed, SeedDecisions.IsTestModeKey(key));

    [Fact]
    public void AVideoOfTheOldLayout_IsConvertedAgain_BeforeAnyLanguage()
    {
        var plan = SeedDecisions.ForLanguages(new VideoDetails(Guid.CreateVersion7(), "Ready", null, "Muxed"));

        Assert.True(plan.ConvertAgain);
        Assert.Equal(TrackStep.None, plan.Dub);
        Assert.Empty(plan.Subtitles);
    }

    [Fact]
    public void WhatIsMissing_IsUploaded_AndWhatAnEarlierRunLeftWaiting_IsMadeAgain()
    {
        var video = new VideoDetails(
            Guid.CreateVersion7(), "Ready", null, "Separate",
            [new(Guid.CreateVersion7(), "tr-TR", "Ready")],
            [new(Guid.CreateVersion7(), "tr-TR", "AwaitingUpload")]);

        var plan = SeedDecisions.ForLanguages(video);

        Assert.False(plan.ConvertAgain);
        Assert.Equal(TrackStep.Upload, plan.Dub);
        Assert.Equal(TrackStep.ReplaceAndUpload, plan.Subtitles["tr-TR"]);
        Assert.Equal(TrackStep.Upload, plan.Subtitles["en-US"]);
    }

    [Theory]
    [InlineData("Ready", "None")]
    [InlineData("Queued", "None")]
    [InlineData("AwaitingUpload", "ReplaceAndUpload")]
    [InlineData("Failed", "Upload")]
    public void TheDub_FollowsItsState(string status, string step)
    {
        var video = new VideoDetails(Guid.CreateVersion7(), "Ready", null, "Separate", [new(Guid.CreateVersion7(), "en-US", status)], []);

        Assert.Equal(Enum.Parse<TrackStep>(step), SeedDecisions.ForLanguages(video).Dub);
    }

    [Fact]
    public void ADubbedMovie_WithEverything_NeedsNothing()
    {
        var video = new VideoDetails(
            Guid.CreateVersion7(), "Ready", null, "Separate",
            [new(Guid.CreateVersion7(), "tr-TR", "Ready"), new(Guid.CreateVersion7(), "en-US", "Ready")],
            [new(Guid.CreateVersion7(), "tr-TR", "Ready"), new(Guid.CreateVersion7(), "en-US", "Ready")]);

        var plan = SeedDecisions.ForLanguages(video);

        Assert.Equal(TrackStep.None, plan.Dub);
        Assert.All(plan.Subtitles.Values, step => Assert.Equal(TrackStep.None, step));
    }

    private static VideoItem Video(string status, int minutesAgo) => new(Guid.CreateVersion7(), status, Now.AddMinutes(-minutesAgo));
}
