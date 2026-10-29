namespace Feed.Domain.Items;

/// <summary>Stores feed items for commands. Queries read through the application's read model instead.</summary>
public interface IFeedItemRepository
{
    Task<bool> ExistsForNoteAsync(Guid noteId, CancellationToken cancellationToken);

    void Add(FeedItem item);
}
