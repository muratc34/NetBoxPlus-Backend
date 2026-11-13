namespace Catalog.Contracts;

/// <summary>
/// What customers can watch of a title now: the whole list, not a change to it. An empty list means
/// nothing (unpublished, or never published). A movie has at most one item, a series one per
/// published episode. Revision goes up with every change of the list or the age rating; a consumer
/// ignores a message whose Revision is not higher than the last it applied for that title.
/// </summary>
/// <param name="Kind"><c>Movie</c> or <c>Series</c>.</param>
/// <param name="AgeRating"><c>All</c>, <c>Age7</c>, <c>Age13</c>, <c>Age16</c> or <c>Age18</c>.</param>
public sealed record PlayableChanged(
    Guid TitleId,
    string Kind,
    string AgeRating,
    IReadOnlyList<PlayableItem> Items,
    int Revision,
    DateTimeOffset OccurredAt);

/// <param name="ContentId">The movie's id, or the episode's.</param>
/// <param name="VideoId">The streaming service's video to play for it.</param>
public sealed record PlayableItem(Guid ContentId, Guid VideoId);
