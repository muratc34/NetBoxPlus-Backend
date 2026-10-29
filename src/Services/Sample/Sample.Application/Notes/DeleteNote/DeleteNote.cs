using Sample.Domain.Notes;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Sample.Application.Notes.DeleteNote;

/// <summary>Soft-deletes a note: the row stays, flagged as deleted, and no longer shows up in reads.</summary>
public sealed record DeleteNoteCommand(Guid Id) : ICommand;

internal sealed class DeleteNoteHandler(INoteRepository notes) : ICommandHandler<DeleteNoteCommand>
{
    public async Task<Result> Handle(DeleteNoteCommand command, CancellationToken cancellationToken)
    {
        var note = await notes.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (note is null)
        {
            return NoteErrors.NotFound;
        }

        notes.Remove(note);
        return Result.Success();
    }
}
