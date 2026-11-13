using Microsoft.EntityFrameworkCore;
using Shared.Application.Validation;
using Shared.Domain;
using Streaming.Application.Abstractions;
using Streaming.Domain.Playback;

namespace Streaming.Application.Playback;

/// <summary>Keeps the position a live session reports as the profile's progress on what it plays.</summary>
internal sealed class PlaybackPositions(IStreamingReadModel readModel, IWatchProgressRepository progress)
{
    private static readonly Error PositionInvalid = Error.Validation("Playback.PositionInvalid", "The position must be a number of seconds, zero or more.");

    public static IEnumerable<ValidationFailure> Check(double? positionSeconds)
    {
        if (positionSeconds is { } position && (!double.IsFinite(position) || position < 0))
        {
            yield return new ValidationFailure("PositionSeconds", PositionInvalid.Code, PositionInvalid.Message);
        }
    }

    public async Task RecordAsync(PlaybackSession session, double? positionSeconds, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (positionSeconds is not { } position)
        {
            return;
        }

        var duration = await readModel.Videos.Where(video => video.Id == session.VideoId).Select(video => video.DurationSeconds).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        await progress.SaveAsync(WatchProgress.Record(session, position, duration ?? 0, now), cancellationToken).ConfigureAwait(false);
    }
}
