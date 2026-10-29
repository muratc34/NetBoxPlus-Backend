using Feed.Domain.Items;

namespace Feed.Application.Abstractions;

/// <summary>Untracked queryable views for query handlers, which project straight to response types.</summary>
public interface IFeedReadModel
{
    IQueryable<FeedItem> Items { get; }
}
