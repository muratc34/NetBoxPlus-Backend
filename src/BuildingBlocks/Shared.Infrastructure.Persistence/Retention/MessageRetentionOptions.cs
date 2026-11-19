namespace Shared.Infrastructure.Persistence.Retention;

/// <summary>How long processed messages are kept. Bound from the <c>MessageRetention</c> section.</summary>
public sealed class MessageRetentionOptions
{
    public const string SectionName = "MessageRetention";

    /// <summary>Turn off where another process cleans the same database (the streaming transcoder).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Sent outbox messages are deleted this long after they were sent. Unsent ones are never deleted.</summary>
    public TimeSpan ProcessedOutbox { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Inbox records are deleted this long after the message was processed. Must stay well above the
    /// longest time a message can be delivered again (RabbitMQ retries take seconds, Stripe retries a
    /// webhook for up to three days); a message arriving after its record is gone is processed again.
    /// </summary>
    public TimeSpan Inbox { get; set; } = TimeSpan.FromDays(30);

    /// <summary>How long to wait between two clean-ups.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Rows deleted per statement, so no statement holds its locks for long.</summary>
    public int BatchSize { get; set; } = 1000;

    internal bool IsValid() =>
        ProcessedOutbox > TimeSpan.Zero && Inbox > TimeSpan.Zero && Interval > TimeSpan.Zero && BatchSize > 0;
}
