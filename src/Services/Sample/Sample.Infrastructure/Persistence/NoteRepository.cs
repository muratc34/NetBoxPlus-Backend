using Microsoft.EntityFrameworkCore;
using Sample.Domain.Notes;

namespace Sample.Infrastructure.Persistence;

internal sealed class NoteRepository(SampleDbContext context) : INoteRepository
{
    public Task<Note?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => context.Notes.FirstOrDefaultAsync(note => note.Id == id, cancellationToken);

    public void Add(Note note) => context.Notes.Add(note);

    public void Remove(Note note) => context.Notes.Remove(note);
}
