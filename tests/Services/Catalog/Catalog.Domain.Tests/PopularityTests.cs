using Catalog.Domain.Popularity;

namespace Catalog.Domain.Tests;

public sealed class PopularityTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ANewerRanking_ReplacesTheCopy_AndNamesWhatChanged()
    {
        var copy = PopularityCopy.Create();
        var (kept, rescored, gone, added) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        copy.Apply(1, Now, [(kept, 4), (rescored, 3), (gone, 2)]);

        var changed = copy.Apply(2, Now.AddHours(1), [(kept, 4), (rescored, 5), (added, 1)]);

        Assert.Equal(new[] { rescored, gone, added }.Order(), changed.Order());
        Assert.Equal((2, Now.AddHours(1)), (copy.Revision, copy.ComputedAt));
        Assert.Equal(
            new Dictionary<Guid, int> { [kept] = 4, [rescored] = 5, [added] = 1 },
            copy.Titles.ToDictionary(title => title.TitleId, title => title.Score));
    }

    [Fact]
    public void TheFirstRanking_ChangesEveryTitleInIt_ButNotOnesNobodyWatched()
    {
        var copy = PopularityCopy.Create();
        var (watched, unwatched) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        var changed = copy.Apply(1, Now, [(watched, 2), (unwatched, 0)]);

        Assert.Equal([watched], changed);
        Assert.Equal(watched, Assert.Single(copy.Titles).TitleId);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(2)]
    public void ARankingThatIsNotNewer_ChangesNothing(int revision)
    {
        var copy = PopularityCopy.Create();
        var title = Guid.CreateVersion7();
        copy.Apply(3, Now, [(title, 4)]);

        var changed = copy.Apply(revision, Now.AddHours(1), [(title, 9), (Guid.CreateVersion7(), 1)]);

        Assert.Empty(changed);
        Assert.Equal((3, Now), (copy.Revision, copy.ComputedAt));
        Assert.Equal((title, 4), (Assert.Single(copy.Titles).TitleId, copy.Titles.Single().Score));
    }
}
