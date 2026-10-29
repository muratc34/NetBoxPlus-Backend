using Shared.Domain;

namespace Feed.Domain.Items;

public static class FeedItemErrors
{
    public static readonly Error NoteIdRequired = Error.Validation("FeedItem.NoteIdRequired", "The note id is required.");

    public static readonly Error TitleRequired = Error.Validation("FeedItem.TitleRequired", "The title is required.");

    public static readonly Error TitleTooLong = Error.Validation(
        "FeedItem.TitleTooLong",
        $"The title must be at most {FeedItem.TitleMaxLength} characters.");
}
