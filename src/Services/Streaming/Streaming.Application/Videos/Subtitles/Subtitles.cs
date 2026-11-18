using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;
using Streaming.Application.Abstractions;
using Streaming.Domain.Videos;

namespace Streaming.Application.Videos.Subtitles;

/// <summary>Promises the upload of subtitles for a converted video and hands out where to send the file.</summary>
public sealed record StartSubtitleUploadCommand(Guid VideoId, string Language) : ICommand<SubtitleUploadTicket>, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => VideoId;
}

/// <param name="UploadAddress">A block blob address for one PUT of a WebVTT or SRT file.</param>
public sealed record SubtitleUploadTicket(Guid SubtitleId, Uri UploadAddress, DateTimeOffset ExpiresAt, long MaxBytes);

/// <summary>Says the file is uploaded: it is read, turned into WebVTT and offered to players at once.</summary>
public sealed record ConfirmSubtitleUploadCommand(Guid VideoId, Guid SubtitleId) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => VideoId;
}

/// <summary>Takes subtitles off the video; the file stays in storage with the video's.</summary>
public sealed record RemoveSubtitleCommand(Guid VideoId, string Language) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => VideoId;
}

internal sealed class StartSubtitleUploadHandler(IVideoRepository videos, IVideoStorage storage, TimeProvider timeProvider)
    : ICommandHandler<StartSubtitleUploadCommand, SubtitleUploadTicket>
{
    public async Task<Result<SubtitleUploadTicket>> Handle(StartSubtitleUploadCommand command, CancellationToken cancellationToken)
    {
        var video = await videos.GetByIdAsync(command.VideoId, cancellationToken).ConfigureAwait(false);
        if (video is null)
        {
            return VideoErrors.NotFound;
        }

        var subtitle = video.StartSubtitle(command.Language, timeProvider.GetUtcNow());
        if (subtitle.IsFailure)
        {
            return subtitle.Error;
        }

        return new SubtitleUploadTicket(
            subtitle.Value.Id,
            storage.CreateUploadAddress(subtitle.Value.Id, subtitle.Value.UploadExpiresAt),
            subtitle.Value.UploadExpiresAt,
            Subtitle.MaxBytes);
    }
}

internal sealed class ConfirmSubtitleUploadHandler(IVideoRepository videos, IVideoStorage storage, TimeProvider timeProvider)
    : ICommandHandler<ConfirmSubtitleUploadCommand>
{
    private const string WebVttContentType = "text/vtt";

    public async Task<Result> Handle(ConfirmSubtitleUploadCommand command, CancellationToken cancellationToken)
    {
        var video = await videos.GetByIdAsync(command.VideoId, cancellationToken).ConfigureAwait(false);
        if (video is null)
        {
            return VideoErrors.NotFound;
        }

        var now = timeProvider.GetUtcNow();
        var subtitle = video.SubtitleAwaitingUpload(command.SubtitleId, now);
        if (subtitle.IsFailure)
        {
            return subtitle.Error;
        }

        var size = await storage.GetUploadSizeAsync(command.SubtitleId, cancellationToken).ConfigureAwait(false);
        if (size is not > 0)
        {
            return VideoErrors.NotUploaded;
        }

        if (size > Subtitle.MaxBytes)
        {
            return VideoErrors.SubtitleTooLarge;
        }

        // A file that is not subtitles stays where it is: the same address takes a corrected upload.
        var vtt = SubtitleFormat.ToWebVtt(await storage.ReadUploadTextAsync(command.SubtitleId, cancellationToken).ConfigureAwait(false));
        if (vtt is null)
        {
            return VideoErrors.SubtitleInvalid;
        }

        await storage.WriteTextAsync(video.Id, subtitle.Value.Target, vtt, WebVttContentType, cancellationToken).ConfigureAwait(false);
        var accepted = video.AcceptSubtitle(command.SubtitleId, now);
        if (accepted.IsSuccess)
        {
            await storage.DeleteUploadAsync(command.SubtitleId, cancellationToken).ConfigureAwait(false);
        }

        return accepted;
    }
}

internal sealed class RemoveSubtitleHandler(IVideoRepository videos) : ICommandHandler<RemoveSubtitleCommand>
{
    public async Task<Result> Handle(RemoveSubtitleCommand command, CancellationToken cancellationToken)
    {
        var video = await videos.GetByIdAsync(command.VideoId, cancellationToken).ConfigureAwait(false);
        return video is null ? VideoErrors.NotFound : video.RemoveSubtitle(command.Language);
    }
}
