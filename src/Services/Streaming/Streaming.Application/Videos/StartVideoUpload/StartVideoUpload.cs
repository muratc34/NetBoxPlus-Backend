using Streaming.Application.Abstractions;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;
using Streaming.Domain.Videos;

namespace Streaming.Application.Videos.StartVideoUpload;

/// <summary>Promises a video upload for a movie or an episode and hands out where to send the file.</summary>
public sealed record StartVideoUploadCommand(ContentKind ContentKind, Guid ContentId) : ICommand<VideoUploadTicket>, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => ContentId;
}

/// <param name="UploadAddress">A block blob address: one PUT for a small file, blocks and a block list for a large one.</param>
public sealed record VideoUploadTicket(Guid VideoId, Uri UploadAddress, DateTimeOffset ExpiresAt, long MaxBytes);

internal sealed class StartVideoUploadHandler(IVideoRepository videos, IVideoStorage storage, TimeProvider timeProvider)
    : ICommandHandler<StartVideoUploadCommand, VideoUploadTicket>
{
    public Task<Result<VideoUploadTicket>> Handle(StartVideoUploadCommand command, CancellationToken cancellationToken)
    {
        var video = Video.StartUpload(command.ContentKind, command.ContentId, timeProvider.GetUtcNow());
        if (video.IsFailure)
        {
            return Task.FromResult<Result<VideoUploadTicket>>(video.Error);
        }

        videos.Add(video.Value);
        return Task.FromResult(Result.Success(new VideoUploadTicket(
            video.Value.Id,
            storage.CreateUploadAddress(video.Value.Id, video.Value.UploadExpiresAt),
            video.Value.UploadExpiresAt,
            Video.MaxBytes)));
    }
}
