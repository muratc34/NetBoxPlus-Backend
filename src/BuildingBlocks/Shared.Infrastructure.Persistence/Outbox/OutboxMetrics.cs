using System.Diagnostics.Metrics;

namespace Shared.Infrastructure.Persistence.Outbox;

/// <summary>The outbox as the monitor last counted it.</summary>
internal sealed record OutboxSnapshot(long Pending, long Abandoned, double OldestPendingAgeSeconds);

/// <summary>
/// The <c>NetBoxPlus.Outbox</c> meter. The gauges report the latest count taken by
/// <see cref="OutboxMonitor{TContext}"/>; reading them never touches the database.
/// </summary>
internal sealed class OutboxMetrics
{
    public const string MeterName = "NetBoxPlus.Outbox";

    private readonly Counter<long> _sent;
    private readonly Counter<long> _sendFailures;
    private volatile OutboxSnapshot? _latest;

    public OutboxMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(MeterName);

        _sent = meter.CreateCounter<long>("netboxplus.outbox.sent", "{message}", "Outbox messages handed to the broker.");
        _sendFailures = meter.CreateCounter<long>("netboxplus.outbox.send_failures", "{message}", "Failed attempts to send an outbox message; each counts towards giving up.");
        meter.CreateObservableGauge("netboxplus.outbox.pending", () => Observe(snapshot => snapshot.Pending), "{message}", "Messages waiting to be sent.");
        meter.CreateObservableGauge("netboxplus.outbox.abandoned", () => Observe(snapshot => snapshot.Abandoned), "{message}", "Messages given up on after too many failed sends; they need attention.");
        meter.CreateObservableGauge("netboxplus.outbox.oldest_pending_age", () => Observe(snapshot => snapshot.OldestPendingAgeSeconds), "s", "How long the oldest waiting message has waited.");
    }

    /// <summary>The latest count, or <see langword="null"/> before the first one.</summary>
    public OutboxSnapshot? Latest
    {
        get => _latest;
        set => _latest = value;
    }

    public void Sent() => _sent.Add(1);

    public void SendFailed() => _sendFailures.Add(1);

    private IEnumerable<Measurement<T>> Observe<T>(Func<OutboxSnapshot, T> value)
        where T : struct
        => _latest is { } snapshot ? [new Measurement<T>(value(snapshot))] : [];
}
