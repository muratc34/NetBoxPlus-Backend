namespace Shared.Infrastructure.Persistence.Inbox;

/// <summary>A message one consumer has processed. The key (message id, consumer) prevents processing it twice.</summary>
public sealed class InboxMessage
{
    public Guid MessageId { get; init; }

    public required string Consumer { get; init; }

    public DateTimeOffset ProcessedAt { get; init; }
}
