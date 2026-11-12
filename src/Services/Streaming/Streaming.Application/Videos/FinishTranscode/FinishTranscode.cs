using Streaming.Application.Abstractions;
using Shared.Application.Cqrs;
using Shared.Domain;
using Streaming.Domain.Videos;

namespace Streaming.Application.Videos.FinishTranscode;

/// <summary>The converter's result: the video is ready to play. Sent by the transcoder, not by people.</summary>
public sealed record MarkVideoReadyCommand(Guid VideoId, double DurationSeconds, IReadOnlyList<Rendition> Renditions) : ICommand;

/// <summary>The converter gave up on the video. Sent by the transcoder, not by people.</summary>
public sealed record MarkVideoFailedCommand(Guid VideoId, string Reason) : ICommand;

internal sealed class MarkVideoReadyHandler(IVideoRepository videos, ITranscodeQueue queue) : ICommandHandler<MarkVideoReadyCommand>
{
    public async Task<Result> Handle(MarkVideoReadyCommand command, CancellationToken cancellationToken)
    {
        var video = await videos.GetByIdAsync(command.VideoId, cancellationToken).ConfigureAwait(false);
        if (video is null)
        {
            return VideoErrors.NotFound;
        }

        var ready = video.MarkReady(command.DurationSeconds, command.Renditions);
        if (ready.IsSuccess)
        {
            queue.Remove(video.Id);
        }

        return ready;
    }
}

internal sealed class MarkVideoFailedHandler(IVideoRepository videos, ITranscodeQueue queue) : ICommandHandler<MarkVideoFailedCommand>
{
    public async Task<Result> Handle(MarkVideoFailedCommand command, CancellationToken cancellationToken)
    {
        var video = await videos.GetByIdAsync(command.VideoId, cancellationToken).ConfigureAwait(false);
        if (video is null)
        {
            return VideoErrors.NotFound;
        }

        var failed = video.MarkFailed(command.Reason);
        if (failed.IsSuccess)
        {
            queue.Remove(video.Id);
        }

        return failed;
    }
}
