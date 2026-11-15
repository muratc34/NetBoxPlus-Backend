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

    private static VideoItem Video(string status, int minutesAgo) => new(Guid.CreateVersion7(), status, Now.AddMinutes(-minutesAgo));
}
