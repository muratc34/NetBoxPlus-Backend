using Streaming.Domain.Ratings;

namespace Streaming.Domain.Tests;

public sealed class RatingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AnotherLevel_IsNewer_TheSameOneIsNot()
    {
        var rating = TitleRating.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), RatingLevel.Like, Now);

        rating.Change(RatingLevel.Like, Now.AddDays(1));
        Assert.Equal((RatingLevel.Like, Now), (rating.Level, rating.RatedAt));

        rating.Change(RatingLevel.Love, Now.AddDays(2));
        Assert.Equal((RatingLevel.Love, Now.AddDays(2)), (rating.Level, rating.RatedAt));
    }

    [Fact]
    public void Taste_PutsLovedBeforeLikedBeforeWatched_EachOnce()
    {
        var (loved, liked, watched, both) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        var taste = Tastes.Order(
            [(liked, RatingLevel.Like), (both, RatingLevel.Like), (loved, RatingLevel.Love)],
            [watched, both],
            Playable(loved, liked, watched, both));

        Assert.Equal([loved, liked, both, watched], taste.Like);
        Assert.Empty(taste.Unlike);
        Assert.Empty(taste.Seen);
    }

    [Fact]
    public void Taste_NeverLikesATitleNotForMe_EvenWhenWatched()
    {
        var (disliked, watched) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        var taste = Tastes.Order([(disliked, RatingLevel.NotForMe)], [disliked, watched], Playable(disliked, watched));

        Assert.Equal([watched], taste.Like);
        Assert.Equal([disliked], taste.Unlike);
        Assert.Empty(taste.Seen);
    }

    [Fact]
    public void Taste_KeepsOnlyWhatCanBeWatched()
    {
        var (gone, kept, goneDisliked) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        var taste = Tastes.Order([(gone, RatingLevel.Love), (goneDisliked, RatingLevel.NotForMe)], [gone, kept], Playable(kept));

        Assert.Equal([kept], taste.Like);
        Assert.Empty(taste.Unlike);
        Assert.Empty(taste.Seen);
    }

    [Fact]
    public void Taste_IsCapped_AndWhatIsLeftOfTheWatchedIsSeen()
    {
        var watched = Enumerable.Range(0, Tastes.MaxLike + Tastes.MaxSeen + 5).Select(_ => Guid.CreateVersion7()).ToList();
        var disliked = Enumerable.Range(0, Tastes.MaxUnlike + 5).Select(_ => Guid.CreateVersion7()).ToList();

        var taste = Tastes.Order([.. disliked.Select(title => (title, RatingLevel.NotForMe))], watched, Playable([.. watched, .. disliked]));

        Assert.Equal(watched.Take(Tastes.MaxLike), taste.Like);
        Assert.Equal(disliked.Take(Tastes.MaxUnlike), taste.Unlike);
        Assert.Equal(watched.Skip(Tastes.MaxLike).Take(Tastes.MaxSeen), taste.Seen);
    }

    private static HashSet<Guid> Playable(params Guid[] titles) => [.. titles];
}
