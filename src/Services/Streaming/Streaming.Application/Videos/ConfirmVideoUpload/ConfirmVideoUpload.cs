using Streaming.Application.Abstractions;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;
using Streaming.Domain.Videos;

namespace Streaming.Application.Videos.ConfirmVideoUpload;

/// <summary>Says the file is uploaded; the video is then converted in the background.</summary>
public sealed record ConfirmVideoUploadCommand(Guid VideoId) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => VideoId;
}

internal sealed class ConfirmVideoUploadHandler(IVideoRepository videos, IVideoStorage storage, ITranscodeQueue queue, TimeProvider timeProvider)
    : ICommandHandler<ConfirmVideoUploadCommand>
{
    public async Task<Result> Handle(ConfirmVideoUploadCommand command, CancellationToken cancellationToken)
    {
        var video = await videos.GetByIdAsync(command.VideoId, cancellationToken).ConfigureAwait(false);
        if (video is null)
        {
            return VideoErrors.NotFound;
        }

        var size = await storage.GetUploadSizeAsync(video.Id, cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        var confirmed = video.ConfirmUpload(size, now);
        if (confirmed.IsFailure)
        {
            return confirmed;
        }

        queue.Enqueue(video.Id, now);
        return Result.Success();
    }
}
