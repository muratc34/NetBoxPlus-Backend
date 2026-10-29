namespace Shared.Infrastructure.Persistence.Outbox;

/// <summary>Settings of the outbox processor. Bound from the <c>Outbox</c> section.</summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>Turn off to keep the processor from sending (for example in tests that inspect the table).</summary>
    public bool Enabled { get; set; } = true;

    public int BatchSize { get; set; } = 50;

    /// <summary>How long to wait before looking again when the last batch was not full.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>After this many failed sends a message is no longer retried and needs attention.</summary>
    public int MaxAttempts { get; set; } = 10;
}
