namespace Sample.Domain.Notes;

/// <summary>Loads and stores notes for commands. Queries read through the application's read model instead.</summary>
public interface INoteRepository
{
    Task<Note?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    void Add(Note note);

    void Remove(Note note);
}
