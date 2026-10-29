using Shared.Domain;

namespace Feed.Domain.Items;

/// <summary>
/// A note in the feed. The feed service learns about notes only from the sample service's events,
/// so an item is a copy of what the event carried; there is one item per note.
/// </summary>
public sealed class FeedItem : AggregateRoot, IAuditableEntity
{
    public const int TitleMaxLength = 200;

    private FeedItem(Guid id, Guid noteId, string title)
        : base(id)
    {
        NoteId = noteId;
        Title = title;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private FeedItem()
    {
    }

    public Guid NoteId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    /// <summary>When the item was added to the feed.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ModifiedAt { get; private set; }

    public static Result<FeedItem> Create(Guid noteId, string? title)
    {
        if (noteId == Guid.Empty)
        {
            return FeedItemErrors.NoteIdRequired;
        }

        var trimmed = title?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return FeedItemErrors.TitleRequired;
        }

        if (trimmed.Length > TitleMaxLength)
        {
            return FeedItemErrors.TitleTooLong;
        }

        return new FeedItem(Guid.CreateVersion7(), noteId, trimmed);
    }
}
