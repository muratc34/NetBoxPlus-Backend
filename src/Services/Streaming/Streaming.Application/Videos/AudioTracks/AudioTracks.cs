using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;
using Streaming.Application.Abstractions;
using Streaming.Domain.Videos;

namespace Streaming.Application.Videos.AudioTracks;

/// <summary>Promises the upload of a dub for a converted video and hands out where to send the file.</summary>
public sealed record StartAudioTrackUploadCommand(Guid VideoId, string Language) : ICommand<AudioTrackUploadTicket>, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => VideoId;
}

/// <param name="UploadAddress">A block blob address, as for the video itself.</param>
public sealed record AudioTrackUploadTicket(Guid TrackId, Uri UploadAddress, DateTimeOffset ExpiresAt, long MaxBytes);

/// <summary>Says the dub is uploaded; it is then converted in the background.</summary>
public sealed record ConfirmAudioTrackUploadCommand(Guid VideoId, Guid TrackId) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => VideoId;
}

/// <summary>Takes a dub off the video; its converted files stay in storage with the video's.</summary>
public sealed record RemoveAudioTrackCommand(Guid VideoId, string Language) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => VideoId;
}

/// <summary>The converter's result for a dub. Sent by the transcoder, not by people.</summary>
public sealed record MarkAudioTrackReadyCommand(Guid VideoId, Guid TrackId, double DurationSeconds, int Bandwidth) : ICommand;

/// <summary>The converter gave up on a dub. Sent by the transcoder, not by people.</summary>
public sealed record MarkAudioTrackFailedCommand(Guid VideoId, Guid TrackId, string Reason) : ICommand;

internal sealed class StartAudioTrackUploadHandler(IVideoRepository videos, IVideoStorage storage, TimeProvider timeProvider)
    : ICommandHandler<StartAudioTrackUploadCommand, AudioTrackUploadTicket>
{
    public async Task<Result<AudioTrackUploadTicket>> Handle(StartAudioTrackUploadCommand command, CancellationToken cancellationToken)
    {
        var video = await videos.GetByIdAsync(command.VideoId, cancellationToken).ConfigureAwait(false);
        if (video is null)
        {
            return VideoErrors.NotFound;
        }

        var track = video.StartAudioTrack(command.Language, timeProvider.GetUtcNow());
        if (track.IsFailure)
        {
            return track.Error;
        }

        // The upload container is shared with videos: track ids are as unique as video ids.
        return new AudioTrackUploadTicket(
            track.Value.Id,
            storage.CreateUploadAddress(track.Value.Id, track.Value.UploadExpiresAt),
            track.Value.UploadExpiresAt,
            Video.MaxBytes);
    }
}

internal sealed class ConfirmAudioTrackUploadHandler(IVideoRepository videos, IVideoStorage storage, ITranscodeQueue queue, TimeProvider timeProvider)
    : ICommandHandler<ConfirmAudioTrackUploadCommand>
{
    public async Task<Result> Handle(ConfirmAudioTrackUploadCommand command, CancellationToken cancellationToken)
    {
        var video = await videos.GetByIdAsync(command.VideoId, cancellationToken).ConfigureAwait(false);
        if (video is null)
        {
            return VideoErrors.NotFound;
        }

        var size = await storage.GetUploadSizeAsync(command.TrackId, cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        var confirmed = video.ConfirmAudioTrack(command.TrackId, size, now);
        if (confirmed.IsFailure)
        {
            return confirmed;
        }

        queue.EnqueueAudioTrack(video.Id, command.TrackId, now);
        return Result.Success();
    }
}

internal sealed class RemoveAudioTrackHandler(IVideoRepository videos, ITranscodeQueue queue) : ICommandHandler<RemoveAudioTrackCommand>
{
    public async Task<Result> Handle(RemoveAudioTrackCommand command, CancellationToken cancellationToken)
    {
        var video = await videos.GetByIdAsync(command.VideoId, cancellationToken).ConfigureAwait(false);
        if (video is null)
        {
            return VideoErrors.NotFound;
        }

        var track = video.AudioTracks.FirstOrDefault(item => item.Language == command.Language);
        var removed = video.RemoveAudioTrack(command.Language);
        if (removed.IsSuccess && track is not null)
        {
            queue.RemoveAudioTrack(track.Id);
        }

        return removed;
    }
}

internal sealed class MarkAudioTrackReadyHandler(IVideoRepository videos, ITranscodeQueue queue) : ICommandHandler<MarkAudioTrackReadyCommand>
{
    public async Task<Result> Handle(MarkAudioTrackReadyCommand command, CancellationToken cancellationToken)
    {
        var video = await videos.GetByIdAsync(command.VideoId, cancellationToken).ConfigureAwait(false);
        if (video is null)
        {
            return VideoErrors.NotFound;
        }

        var ready = video.MarkAudioTrackReady(command.TrackId, command.DurationSeconds, command.Bandwidth);
        if (ready.IsSuccess)
        {
            queue.RemoveAudioTrack(command.TrackId);
        }

        return ready;
    }
}

internal sealed class MarkAudioTrackFailedHandler(IVideoRepository videos, ITranscodeQueue queue) : ICommandHandler<MarkAudioTrackFailedCommand>
{
    public async Task<Result> Handle(MarkAudioTrackFailedCommand command, CancellationToken cancellationToken)
    {
        var video = await videos.GetByIdAsync(command.VideoId, cancellationToken).ConfigureAwait(false);
        if (video is null)
        {
            return VideoErrors.NotFound;
        }

        var failed = video.MarkAudioTrackFailed(command.TrackId, command.Reason);
        if (failed.IsSuccess)
        {
            queue.RemoveAudioTrack(command.TrackId);
        }

        return failed;
    }
}
