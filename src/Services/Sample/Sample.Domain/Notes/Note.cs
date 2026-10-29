using Shared.Domain;

namespace Sample.Domain.Notes;

/// <summary>A titled piece of text. The sample service's only aggregate.</summary>
public sealed class Note : AggregateRoot, IAuditableEntity, ISoftDeletableEntity
{
    public const int ContentMaxLength = 4000;

    private Note(Guid id, NoteTitle title, string content)
        : base(id)
    {
        Title = title;
        Content = content;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private Note()
    {
    }

    public NoteTitle Title { get; private set; } = null!;

    public string Content { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ModifiedAt { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public static Note Create(NoteTitle title, string content)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(content);

        var note = new Note(Guid.CreateVersion7(), title, content);
        note.RaiseDomainEvent(new NoteCreatedDomainEvent(note.Id, title.Value));
        return note;
    }
}

public sealed record NoteCreatedDomainEvent(Guid NoteId, string Title) : IDomainEvent;
