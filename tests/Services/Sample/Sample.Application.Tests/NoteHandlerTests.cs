using Sample.Application.Notes.CreateNote;
using Sample.Application.Notes.DeleteNote;
using Sample.Contracts;
using Sample.Domain.Notes;
using Shared.Application.Events;

namespace Sample.Application.Tests;

public sealed class CreateNoteTests
{
    private readonly FakeNoteRepository _notes = new();

    [Fact]
    public async Task ValidCommand_AddsTheNoteAndReturnsItsId()
    {
        var result = await new CreateNoteHandler(_notes).Handle(new CreateNoteCommand("Shopping list", "Milk"), TestContext.Current.CancellationToken);

        var note = Assert.Single(_notes.Notes);
        Assert.Equal(note.Id, result.Value);
        Assert.Equal("Shopping list", note.Title.Value);
    }

    [Fact]
    public async Task TooLongTitle_FailsWithoutAddingANote()
    {
        var result = await new CreateNoteHandler(_notes).Handle(
            new CreateNoteCommand(new string('a', NoteTitle.MaxLength + 1), "Milk"),
            TestContext.Current.CancellationToken);

        Assert.Equal(NoteErrors.TitleTooLong, result.Error);
        Assert.Empty(_notes.Notes);
    }

    [Theory]
    [InlineData("", "Milk", "Title")]
    [InlineData("Shopping list", null, "Content")]
    public void Validator_ReportsMissingFields(string title, string? content, string property)
    {
        var failure = Assert.Single(new CreateNoteValidator().Validate(new CreateNoteCommand(title, content!)));

        Assert.Equal(property, failure.PropertyName);
    }

    [Fact]
    public void Validator_RejectsTooLongContent()
    {
        var failure = Assert.Single(new CreateNoteValidator().Validate(new CreateNoteCommand("Title", new string('a', Note.ContentMaxLength + 1))));

        Assert.Equal("Note.ContentTooLong", failure.Code);
    }

    [Fact]
    public async Task NoteCreated_IsPublishedAsAnIntegrationEvent()
    {
        var publisher = new FakePublisher();
        var noteId = Guid.CreateVersion7();

        await new PublishNoteCreated(publisher).Handle(new NoteCreatedDomainEvent(noteId, "Shopping list"), TestContext.Current.CancellationToken);

        Assert.Equal(new NoteCreated(noteId, "Shopping list"), Assert.Single(publisher.Published));
    }
}

public sealed class DeleteNoteTests
{
    private readonly FakeNoteRepository _notes = new();

    [Fact]
    public async Task ExistingNote_IsRemoved()
    {
        var note = Note.Create(NoteTitle.Create("Shopping list").Value, "Milk");
        _notes.Add(note);

        var result = await new DeleteNoteHandler(_notes).Handle(new DeleteNoteCommand(note.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(_notes.Notes);
    }

    [Fact]
    public async Task UnknownNote_IsNotFound()
    {
        var result = await new DeleteNoteHandler(_notes).Handle(new DeleteNoteCommand(Guid.CreateVersion7()), TestContext.Current.CancellationToken);

        Assert.Equal(NoteErrors.NotFound, result.Error);
    }
}

internal sealed class FakeNoteRepository : INoteRepository
{
    public List<Note> Notes { get; } = [];

    public Task<Note?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(Notes.SingleOrDefault(note => note.Id == id));

    public void Add(Note note) => Notes.Add(note);

    public void Remove(Note note) => Notes.Remove(note);
}

internal sealed class FakePublisher : IIntegrationEventPublisher
{
    public List<object> Published { get; } = [];

    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        Published.Add(integrationEvent);
        return Task.CompletedTask;
    }
}
