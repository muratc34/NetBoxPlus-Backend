using Streaming.Application.Abstractions;
using Streaming.Infrastructure.Persistence;

namespace Streaming.Infrastructure.Media;

/// <summary>
/// A video, or a dub of one, waiting for the converter. A worker takes it for a while (<see cref="LeasedUntil"/>); if
/// the worker dies the lease runs out and another takes it. The row goes with the result.
/// </summary>
internal sealed class TranscodeJob
{
    public const int LastErrorMaxLength = 1000;

    /// <summary>The video's id for the video itself, the track's for a dub.</summary>
    public Guid Id { get; init; }

    public Guid VideoId { get; init; }

    /// <summary>Set for a dub.</summary>
    public Guid? AudioTrackId { get; init; }

    /// <summary>Not before this: a failed attempt waits before the next.</summary>
    public DateTimeOffset AvailableAt { get; set; }

    public DateTimeOffset? LeasedUntil { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }
}

internal sealed class EfTranscodeQueue(StreamingDbContext context) : ITranscodeQueue
{
    public void Enqueue(Guid videoId, DateTimeOffset now)
        => context.Set<TranscodeJob>().Add(new TranscodeJob { Id = videoId, VideoId = videoId, AvailableAt = now });

    public void Remove(Guid videoId) => RemoveJob(videoId);

    public void EnqueueAudioTrack(Guid videoId, Guid trackId, DateTimeOffset now)
        => context.Set<TranscodeJob>().Add(new TranscodeJob { Id = trackId, VideoId = videoId, AudioTrackId = trackId, AvailableAt = now });

    public void RemoveAudioTrack(Guid trackId) => RemoveJob(trackId);

    private void RemoveJob(Guid id)
    {
        var job = context.Set<TranscodeJob>().Local.FirstOrDefault(item => item.Id == id)
            ?? context.Set<TranscodeJob>().Find(id);
        if (job is not null)
        {
            context.Set<TranscodeJob>().Remove(job);
        }
    }
}
