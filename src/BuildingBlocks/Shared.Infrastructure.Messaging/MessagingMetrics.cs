using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Shared.Infrastructure.Messaging;

/// <summary>How a delivery ended, as the <c>outcome</c> tag spells it.</summary>
internal static class MessageOutcomes
{
    /// <summary>Handled and acknowledged; a handler's permanent rejection also ends here, through the inbox.</summary>
    public const string Processed = "processed";

    /// <summary>A handler threw; the message waits and is tried again.</summary>
    public const string Retried = "retried";

    public const string DeadLettered = "dead_lettered";
}

/// <summary>
/// The <c>NetBoxPlus.Messaging</c> meter. Tags carry the message type and the queue, never an id.
/// The dead-letter gauge reports the latest count taken by <see cref="DeadLetterMonitor"/>.
/// </summary>
internal sealed class MessagingMetrics
{
    public const string MeterName = "NetBoxPlus.Messaging";

    private readonly Counter<long> _handled;
    private readonly Histogram<double> _duration;
    private volatile DeadLetterCount? _deadLetters;

    public MessagingMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(MeterName);

        _handled = meter.CreateCounter<long>("netboxplus.messages.handled", "{message}", "Deliveries by how they ended: processed, retried or dead-lettered.");
        _duration = meter.CreateHistogram<double>("netboxplus.messages.handling_duration", "s", "Time from a delivery to its acknowledgement, retries included.");
        meter.CreateObservableGauge(
            "netboxplus.messaging.dead_letter_depth",
            () => _deadLetters is { } count ? [new Measurement<long>(count.Messages, new KeyValuePair<string, object?>("queue", count.Queue))] : Array.Empty<Measurement<long>>(),
            "{message}",
            "Messages in the service's dead-letter queue; each is an event a handler could not take.");
    }

    /// <summary>The latest dead-letter count, or <see langword="null"/> before the first one.</summary>
    public DeadLetterCount? DeadLetters
    {
        get => _deadLetters;
        set => _deadLetters = value;
    }

    public void Handled(string messageType, string outcome)
        => _handled.Add(1, new TagList { { "message_type", messageType }, { "outcome", outcome } });

    public void HandlingTook(string messageType, TimeSpan elapsed)
        => _duration.Record(elapsed.TotalSeconds, new TagList { { "message_type", messageType } });
}

internal sealed record DeadLetterCount(string Queue, long Messages);
