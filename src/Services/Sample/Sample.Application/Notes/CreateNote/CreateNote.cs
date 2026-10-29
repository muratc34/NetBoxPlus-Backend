using Sample.Domain.Notes;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Sample.Application.Notes.CreateNote;

public sealed record CreateNoteCommand(string Title, string Content) : ICommand<Guid>;

internal sealed class CreateNoteValidator : IValidator<CreateNoteCommand>
{
    public IEnumerable<ValidationFailure> Validate(CreateNoteCommand instance)
    {
        // Only the shape of the request; the title's rules live in NoteTitle.
        if (string.IsNullOrWhiteSpace(instance.Title))
        {
            yield return new ValidationFailure(nameof(instance.Title), NoteErrors.TitleRequired.Code, NoteErrors.TitleRequired.Message);
        }

        if (instance.Content is null)
        {
            yield return new ValidationFailure(nameof(instance.Content), "Note.ContentRequired", "The content is required.");
        }
        else if (instance.Content.Length > Note.ContentMaxLength)
        {
            yield return new ValidationFailure(
                nameof(instance.Content),
                "Note.ContentTooLong",
                $"The content must be at most {Note.ContentMaxLength} characters.");
        }
    }
}

internal sealed class CreateNoteHandler(INoteRepository notes) : ICommandHandler<CreateNoteCommand, Guid>
{
    public Task<Result<Guid>> Handle(CreateNoteCommand command, CancellationToken cancellationToken)
    {
        var title = NoteTitle.Create(command.Title);
        if (title.IsFailure)
        {
            return Task.FromResult(Result.Failure<Guid>(title.Error));
        }

        var note = Note.Create(title.Value, command.Content);
        notes.Add(note);

        return Task.FromResult(Result.Success(note.Id));
    }
}
