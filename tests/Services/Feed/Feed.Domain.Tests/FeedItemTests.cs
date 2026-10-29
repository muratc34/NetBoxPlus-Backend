using Feed.Domain.Items;

namespace Feed.Domain.Tests;

public sealed class FeedItemTests
{
    [Fact]
    public void Create_KeepsTheNoteAndTrimsTheTitle()
    {
        var noteId = Guid.CreateVersion7();

        var item = FeedItem.Create(noteId, "  Shopping list ").Value;

        Assert.Equal(noteId, item.NoteId);
        Assert.Equal("Shopping list", item.Title);
        Assert.Equal(7, item.Id.Version);
    }

    [Fact]
    public void EmptyNoteId_IsRejected()
    {
        Assert.Equal(FeedItemErrors.NoteIdRequired, FeedItem.Create(Guid.Empty, "Title").Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankTitle_IsRejected(string? title)
    {
        Assert.Equal(FeedItemErrors.TitleRequired, FeedItem.Create(Guid.CreateVersion7(), title).Error);
    }

    [Fact]
    public void TooLongTitle_IsRejected()
    {
        var result = FeedItem.Create(Guid.CreateVersion7(), new string('a', FeedItem.TitleMaxLength + 1));

        Assert.Equal(FeedItemErrors.TitleTooLong, result.Error);
    }

    [Fact]
    public void TitleAtTheLimit_IsAccepted()
    {
        Assert.True(FeedItem.Create(Guid.CreateVersion7(), new string('a', FeedItem.TitleMaxLength)).IsSuccess);
    }
}
