using Feed.Domain.Items;
using Microsoft.EntityFrameworkCore;

namespace Feed.Infrastructure.Persistence;

internal sealed class FeedItemRepository(FeedDbContext context) : IFeedItemRepository
{
    public Task<bool> ExistsForNoteAsync(Guid noteId, CancellationToken cancellationToken)
        => context.Items.AnyAsync(item => item.NoteId == noteId, cancellationToken);

    public void Add(FeedItem item) => context.Items.Add(item);
}
