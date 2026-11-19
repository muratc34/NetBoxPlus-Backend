namespace Shared.Infrastructure.Persistence.Outbox;

/// <summary>
/// An integration event waiting to be sent. Written in the same transaction as the change that
/// published it; the outbox processor sends it and marks it processed.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; }

    public required string Type { get; init; }

    public required string Payload { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public string? TraceParent { get; init; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }
}
