using Streaming.Domain.Popularity;

namespace Streaming.Domain.Tests;

public sealed class PopularityTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = new(2026, 11, 18);

    [Fact]
    public void TheRanking_IsHighestScoreFirst_WithoutTitlesNobodyWatched()
    {
        var ranking = PopularityRanking.Create();
        var (low, high, tieA, tieB, none) = (Id(1), Id(2), Id(3), Id(4), Id(5));

        ranking.Update([new(low, 1), new(tieB, 4), new(none, 0), new(high, 9), new(tieA, 4)], Today, Now);

        var changed = Assert.IsType<PopularityChangedDomainEvent>(Assert.Single(ranking.DomainEvents));
        Assert.Equal([new(high, 9), new(tieA, 4), new(tieB, 4), new(low, 1)], changed.Titles);
        Assert.Equal((1, Now), (changed.Revision, changed.ComputedAt));
        Assert.Equal((1, 1, Now, Today.AddDays(-1)), (ranking.Revision, ranking.Version, ranking.ComputedAt, ranking.CountedThrough));
        Assert.Equal(
            [(high, 9), (tieA, 4), (tieB, 4), (low, 1)],
            ranking.Titles.OrderByDescending(title => title.Score).ThenBy(title => title.TitleId).Select(title => (title.TitleId, title.Score)));
    }

    [Fact]
    public void TheRanking_KeepsAHundredTitles()
    {
        var ranking = PopularityRanking.Create();

        ranking.Update(Enumerable.Range(1, PopularityRanking.MaxTitles + 1).Select(score => new TitleScore(Id(score), score)), Today, Now);

        Assert.Equal(PopularityRanking.MaxTitles, ranking.Titles.Count);
        Assert.DoesNotContain(ranking.Titles, title => title.Score == 1);
    }

    [Fact]
    public void TheSameRanking_IsNotPublishedAgain_ButTheComputationIsRecorded()
    {
        var ranking = PopularityRanking.Create();
        var title = Id(1);
        ranking.Update([new(title, 3)], Today, Now);
        ranking.ClearDomainEvents();

        ranking.Update([new(title, 3)], Today.AddDays(1), Now.AddHours(1));

        Assert.Empty(ranking.DomainEvents);
        Assert.Equal((1, 2, Now.AddHours(1), Today), (ranking.Revision, ranking.Version, ranking.ComputedAt, ranking.CountedThrough));
    }

    [Fact]
    public void AChangedRanking_ReplacesTheOldOne()
    {
        var ranking = PopularityRanking.Create();
        var (kept, gone, added) = (Id(1), Id(2), Id(3));
        ranking.Update([new(kept, 3), new(gone, 2)], Today, Now);
        ranking.ClearDomainEvents();

        ranking.Update([new(kept, 5), new(added, 1)], Today, Now.AddHours(1));

        var changed = Assert.IsType<PopularityChangedDomainEvent>(Assert.Single(ranking.DomainEvents));
        Assert.Equal(2, changed.Revision);
        Assert.Equal([new(kept, 5), new(added, 1)], changed.Titles);
        Assert.Equal([(kept, 5), (added, 1)], ranking.Titles.OrderByDescending(title => title.Score).Select(title => (title.TitleId, title.Score)));
    }

    [Fact]
    public void OnlyAScoreThatChanged_IsAChange()
    {
        var ranking = PopularityRanking.Create();
        var (first, second) = (Id(1), Id(2));
        ranking.Update([new(first, 3), new(second, 2)], Today, Now);
        ranking.ClearDomainEvents();

        ranking.Update([new(first, 3), new(second, 1)], Today, Now);

        Assert.Single(ranking.DomainEvents);
    }

    [Fact]
    public void ATitleThatLeftTheRanking_IsAChange()
    {
        var ranking = PopularityRanking.Create();
        var (stays, leaves) = (Id(1), Id(2));
        ranking.Update([new(stays, 3), new(leaves, 2)], Today, Now);
        ranking.ClearDomainEvents();

        ranking.Update([new(stays, 3)], Today, Now);

        var changed = Assert.IsType<PopularityChangedDomainEvent>(Assert.Single(ranking.DomainEvents));
        Assert.Equal([new(stays, 3)], changed.Titles);
        Assert.Equal(stays, Assert.Single(ranking.Titles).TitleId);
    }

    [Fact]
    public void AComputation_IsDueOnceTheGapHasPassed()
    {
        var ranking = PopularityRanking.Create();
        Assert.True(ranking.IsDue(Now, TimeSpan.FromHours(1)));

        ranking.Update([], Today, Now);

        Assert.False(ranking.IsDue(Now.AddMinutes(59), TimeSpan.FromHours(1)));
        Assert.True(ranking.IsDue(Now.AddHours(1), TimeSpan.FromHours(1)));
    }

    [Fact]
    public void TheFinishedDaysOfTheWindow_AreCountedOnce()
    {
        var ranking = PopularityRanking.Create();
        Assert.Equal(Enumerable.Range(1, 6).Select(back => Today.AddDays(-7 + back)), ranking.DaysToCount(Today));
        Assert.Equal(Today.AddDays(-6), PopularityRanking.FirstDay(Today));

        ranking.Update([], Today, Now);
        Assert.Empty(ranking.DaysToCount(Today));
        Assert.Equal([Today], ranking.DaysToCount(Today.AddDays(1)));
        Assert.Equal([Today, Today.AddDays(1)], ranking.DaysToCount(Today.AddDays(2)));
        Assert.Equal(6, ranking.DaysToCount(Today.AddDays(30)).Count());
        Assert.Equal(Today.AddDays(24), ranking.DaysToCount(Today.AddDays(30)).First());
    }

    private static Guid Id(int number) => new($"00000000-0000-0000-0000-{number:D12}");
}
