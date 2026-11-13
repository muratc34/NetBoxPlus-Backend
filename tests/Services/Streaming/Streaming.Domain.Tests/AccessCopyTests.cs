using Streaming.Domain.Access;

namespace Streaming.Domain.Tests;

/// <summary>Copies of other services' data: messages come late, twice or out of order.</summary>
public sealed class AccessCopyTests
{
    [Fact]
    public void ASubscription_FollowsTheNewestRevision_AndIgnoresOlderNews()
    {
        var subscription = SubscriptionCopy.Create(Guid.CreateVersion7(), Guid.CreateVersion7());

        Assert.True(subscription.Activate(VideoQuality.Hd, 2, 1));
        Assert.True(subscription.Stop(SubscriptionStatus.Suspended, 3));
        Assert.False(subscription.Activate(VideoQuality.Uhd, 4, 2));
        Assert.False(subscription.Stop(SubscriptionStatus.Canceled, 3));

        Assert.Equal((SubscriptionStatus.Suspended, VideoQuality.Hd, 2, 3, false), (subscription.Status, subscription.Quality, subscription.MaxScreens, subscription.Revision, subscription.GivesAccess));
    }

    [Fact]
    public void TheSameRevisionAgain_ChangesNothing()
    {
        var subscription = SubscriptionCopy.Create(Guid.CreateVersion7(), Guid.CreateVersion7());
        subscription.Activate(VideoQuality.Sd, 1, 1);

        Assert.False(subscription.Activate(VideoQuality.Uhd, 4, 1));
        Assert.Equal((VideoQuality.Sd, 1), (subscription.Quality, subscription.MaxScreens));
    }

    [Fact]
    public void ARecoveredSubscription_GivesAccessAgain_WithItsNewPlan()
    {
        var subscription = SubscriptionCopy.Create(Guid.CreateVersion7(), Guid.CreateVersion7());
        subscription.Activate(VideoQuality.Sd, 1, 1);
        subscription.Stop(SubscriptionStatus.Suspended, 2);

        subscription.Activate(VideoQuality.Uhd, 4, 3);

        Assert.True(subscription.GivesAccess);
        Assert.Equal((VideoQuality.Uhd, 4), (subscription.Quality, subscription.MaxScreens));
    }

    [Fact]
    public void TheFirstNews_IsTakenWhateverItsRevision()
    {
        var subscription = SubscriptionCopy.Create(Guid.CreateVersion7(), Guid.CreateVersion7());

        Assert.True(subscription.Stop(SubscriptionStatus.Canceled, 0));
        Assert.Equal(SubscriptionStatus.Canceled, subscription.Status);
    }

    [Fact]
    public void Stopping_WithAnActiveStatus_IsAMistake()
        => Assert.Throws<ArgumentOutOfRangeException>(() => SubscriptionCopy.Create(Guid.CreateVersion7(), Guid.CreateVersion7()).Stop(SubscriptionStatus.Active, 1));

    [Fact]
    public void ARemovedProfile_StaysRemoved_WhenOlderNewsArrives()
    {
        var profile = ProfileCopy.Create(Guid.CreateVersion7(), Guid.CreateVersion7());
        profile.Apply(AgeRating.Age13, 1);

        Assert.True(profile.Remove(3));
        Assert.False(profile.Apply(AgeRating.Age18, 2));

        Assert.Equal((true, AgeRating.Age13, 3), (profile.IsRemoved, profile.MaxAgeRating, profile.Revision));
    }

    [Fact]
    public void AProfile_TakesItsNewestAgeRating()
    {
        var profile = ProfileCopy.Create(Guid.CreateVersion7(), Guid.CreateVersion7());

        profile.Apply(AgeRating.Age7, 2);
        Assert.False(profile.Apply(AgeRating.All, 1));
        Assert.False(profile.Apply(AgeRating.All, 2));
        profile.Apply(AgeRating.Age16, 3);

        Assert.Equal(AgeRating.Age16, profile.MaxAgeRating);
    }

    [Fact]
    public void ATitle_IsReplacedWhole_ByANewerList()
    {
        var title = PlayableTitle.Create(Guid.CreateVersion7(), TitleKind.Series);
        var first = new PlayableItem(Guid.CreateVersion7(), Guid.CreateVersion7());
        var second = new PlayableItem(Guid.CreateVersion7(), Guid.CreateVersion7());

        Assert.True(title.Replace(AgeRating.Age13, [first, second], 1));
        Assert.True(title.Replace(AgeRating.Age16, [second], 2));
        Assert.False(title.Replace(AgeRating.All, [first], 1));

        Assert.Equal(AgeRating.Age16, title.AgeRating);
        Assert.Equal([second], title.Items);
    }

    [Fact]
    public void AnEmptyList_LeavesNothingToWatch()
    {
        var title = PlayableTitle.Create(Guid.CreateVersion7(), TitleKind.Movie);
        title.Replace(AgeRating.All, [new PlayableItem(title.Id, Guid.CreateVersion7())], 1);

        title.Replace(AgeRating.All, [], 2);

        Assert.Empty(title.Items);
    }
}
