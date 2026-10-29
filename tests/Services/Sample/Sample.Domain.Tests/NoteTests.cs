using Sample.Domain.Notes;

namespace Sample.Domain.Tests;

public sealed class NoteTitleTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankTitle_IsRejected(string? value)
    {
        Assert.Equal(NoteErrors.TitleRequired, NoteTitle.Create(value).Error);
    }

    [Fact]
    public void TooLongTitle_IsRejected()
    {
        Assert.Equal(NoteErrors.TitleTooLong, NoteTitle.Create(new string('a', NoteTitle.MaxLength + 1)).Error);
    }

    [Fact]
    public void TitleAtTheLimit_IsAccepted()
    {
        Assert.True(NoteTitle.Create(new string('a', NoteTitle.MaxLength)).IsSuccess);
    }

    [Fact]
    public void Title_IsTrimmed()
    {
        Assert.Equal("Shopping list", NoteTitle.Create("  Shopping list ").Value.Value);
    }
}

public sealed class NoteTests
{
    [Fact]
    public void Create_RaisesNoteCreated()
    {
        var note = Note.Create(NoteTitle.Create("Shopping list").Value, "Milk, eggs");

        var raised = Assert.IsType<NoteCreatedDomainEvent>(Assert.Single(note.DomainEvents));
        Assert.Equal(note.Id, raised.NoteId);
        Assert.Equal("Shopping list", raised.Title);
    }

    // Version 7 ids sort by creation time across milliseconds; within one millisecond the
    // remaining bits are random, so two ids created back to back have no guaranteed order.
    [Fact]
    public void Create_AssignsAVersion7Id()
    {
        var note = Note.Create(NoteTitle.Create("First").Value, string.Empty);

        Assert.Equal(7, note.Id.Version);
    }
}
