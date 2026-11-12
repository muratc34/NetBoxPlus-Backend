using Streaming.Application.Abstractions;
using Streaming.Infrastructure.Persistence;

namespace Streaming.Infrastructure.Media;

/// <summary>
/// A video waiting for the converter. A worker takes it for a while (<see cref="LeasedUntil"/>); if
/// the worker dies the lease runs out and another takes it. The row goes with the result.
/// </summary>
internal sealed class TranscodeJob
{
    public const int LastErrorMaxLength = 1000;

    public Guid VideoId { get; init; }

    /// <summary>Not before this: a failed attempt waits before the next.</summary>
    public DateTimeOffset AvailableAt { get; set; }

    public DateTimeOffset? LeasedUntil { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }
}

internal sealed class EfTranscodeQueue(StreamingDbContext context) : ITranscodeQueue
{
    public void Enqueue(Guid videoId, DateTimeOffset now)
        => context.Set<TranscodeJob>().Add(new TranscodeJob { VideoId = videoId, AvailableAt = now });

    public void Remove(Guid videoId)
    {
        var job = context.Set<TranscodeJob>().Local.FirstOrDefault(item => item.VideoId == videoId)
            ?? context.Set<TranscodeJob>().Find(videoId);
        if (job is not null)
        {
            context.Set<TranscodeJob>().Remove(job);
        }
    }
}
