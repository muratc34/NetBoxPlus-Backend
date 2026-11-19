using Streaming.Domain.Lists;

namespace Streaming.Domain.Tests;

public sealed class MyListTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ATitle_IsSavedOnce_AndKeepsWhenItWasAdded()
    {
        var list = MyList.Create(Guid.CreateVersion7(), Guid.CreateVersion7());
        var title = Guid.CreateVersion7();

        Assert.True(list.Add(title, Now).IsSuccess);
        Assert.True(list.Add(title, Now.AddDays(1)).IsSuccess);

        var entry = Assert.Single(list.Entries);
        Assert.Equal((list.Id, title, Now), (entry.ProfileId, entry.TitleId, entry.AddedAt));
        Assert.Equal(1, list.Version);
    }

    [Fact]
    public void AFullList_TakesNoNewTitle_ButStillTheSavedOnes()
    {
        var list = MyList.Create(Guid.CreateVersion7(), Guid.CreateVersion7());
        var first = Guid.CreateVersion7();
        list.Add(first, Now);
        for (var i = 1; i < MyList.MaxEntries; i++)
        {
            Assert.True(list.Add(Guid.CreateVersion7(), Now).IsSuccess);
        }

        Assert.Equal(MyListErrors.Full, list.Add(Guid.CreateVersion7(), Now).Error);
        Assert.True(list.Add(first, Now).IsSuccess);
        Assert.Equal(MyList.MaxEntries, list.Entries.Count);
    }

    [Fact]
    public void Removing_ChangesTheVersion_OnlyWhenATitleWentOff()
    {
        var list = MyList.Create(Guid.CreateVersion7(), Guid.CreateVersion7());
        var title = Guid.CreateVersion7();
        list.Add(title, Now);

        list.Remove(Guid.CreateVersion7());
        Assert.Equal(1, list.Version);

        list.Remove(title);
        Assert.Empty(list.Entries);
        Assert.Equal(2, list.Version);
    }

    [Fact]
    public void AListWithoutAProfileOrUser_IsAProgrammingError()
    {
        Assert.ThrowsAny<ArgumentException>(() => MyList.Create(Guid.Empty, Guid.CreateVersion7()));
        Assert.ThrowsAny<ArgumentException>(() => MyList.Create(Guid.CreateVersion7(), Guid.Empty));
    }
}
