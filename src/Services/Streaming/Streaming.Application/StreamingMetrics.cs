using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Streaming.Application;

/// <summary>How a transcode job ended, as the <c>outcome</c> tag spells it.</summary>
public static class TranscodeOutcomes
{
    public const string Converted = "converted";

    /// <summary>The file cannot be converted; failed at once.</summary>
    public const string Rejected = "rejected";

    /// <summary>Trouble around the file; the job is tried again later.</summary>
    public const string Retried = "retried";

    /// <summary>Tried too many times; the video failed.</summary>
    public const string GaveUp = "gave_up";
}

/// <summary>Transcode jobs and playback sessions as last counted from the database.</summary>
public sealed record StreamingSnapshot(long WaitingJobs, long RunningJobs, long ActiveSessions);

/// <summary>
/// The <c>NetBoxPlus.Streaming</c> meter. Tags carry outcomes and error codes, never an id. The
/// gauges report the latest count taken in the background; reading them never touches the database.
/// </summary>
public sealed class StreamingMetrics
{
    public const string MeterName = "NetBoxPlus.Streaming";

    private readonly Counter<long> _transcodes;
    private readonly Histogram<double> _transcodeDuration;
    private readonly Counter<long> _playbackStarts;
    private readonly Counter<long> _trailerRequests;
    private volatile StreamingSnapshot? _latest;

    public StreamingMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(MeterName);

        _transcodes = meter.CreateCounter<long>("netboxplus.transcode.completed", "{job}", "Transcode attempts by outcome.");
        _transcodeDuration = meter.CreateHistogram<double>("netboxplus.transcode.duration", "s", "Time to download, convert and upload one video.");
        _playbackStarts = meter.CreateCounter<long>("netboxplus.playback.sessions.started", "{session}", "Playback starts: 'started', or the error code that refused one.");
        _trailerRequests = meter.CreateCounter<long>("netboxplus.streaming.trailer.requests", "{request}", "Trailer links asked for: 'found', or 'not_found' for a title without a playable trailer.");
        meter.CreateObservableGauge(
            "netboxplus.transcode.jobs",
            () => _latest is { } snapshot
                ? [new Measurement<long>(snapshot.WaitingJobs, new KeyValuePair<string, object?>("state", "waiting")), new Measurement<long>(snapshot.RunningJobs, new KeyValuePair<string, object?>("state", "running"))]
                : Array.Empty<Measurement<long>>(),
            "{job}",
            "Videos waiting to be converted, and being converted.");
        meter.CreateObservableGauge(
            "netboxplus.playback.sessions.active",
            () => _latest is { } snapshot ? [new Measurement<long>(snapshot.ActiveSessions)] : Array.Empty<Measurement<long>>(),
            "{session}",
            "Playback sessions with a recent heartbeat.");
    }

    /// <summary>The latest count, or <see langword="null"/> before the first one.</summary>
    public StreamingSnapshot? Latest
    {
        get => _latest;
        set => _latest = value;
    }

    public void Transcoded(string outcome, TimeSpan elapsed)
    {
        var tags = new TagList { { "outcome", outcome } };
        _transcodes.Add(1, tags);
        _transcodeDuration.Record(elapsed.TotalSeconds, tags);
    }

    public void PlaybackStarted(string outcome) => _playbackStarts.Add(1, new TagList { { "outcome", outcome } });

    public void TrailerRequested(string outcome) => _trailerRequests.Add(1, new TagList { { "outcome", outcome } });
}
