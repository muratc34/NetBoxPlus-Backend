using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;
using Streaming.Application.Abstractions;
using Streaming.Domain.Playback;

namespace Streaming.Application.Playback.Heartbeat;

/// <summary>
/// The player is still watching, at <paramref name="PositionSeconds"/> when it says so. The rules are
/// checked again: a subscription that stopped, a title taken down or a profile removed ends the session.
/// </summary>
public sealed record HeartbeatCommand(Guid UserId, Guid SessionId, double? PositionSeconds = null) : ICommand;

internal sealed class HeartbeatValidator : IValidator<HeartbeatCommand>
{
    public IEnumerable<ValidationFailure> Validate(HeartbeatCommand instance)
        => PlaybackPositions.Check(instance.PositionSeconds);
}

internal sealed class HeartbeatHandler(
    IStreamingReadModel readModel,
    PlaybackGate gate,
    IPlaybackSessionRepository sessions,
    PlaybackPositions positions,
    TimeProvider timeProvider)
    : ICommandHandler<HeartbeatCommand>
{
    public async Task<Result> Handle(HeartbeatCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var session = await readModel.PlaybackSessions
            .FirstOrDefaultAsync(item => item.Id == command.SessionId && item.UserId == command.UserId, cancellationToken).ConfigureAwait(false);
        if (session is null || !session.IsLive(now))
        {
            return PlaybackErrors.SessionEnded;
        }

        var access = await gate.CheckAsync(command.UserId, session.ProfileId, session.ContentId, cancellationToken).ConfigureAwait(false);
        if (access.IsFailure)
        {
            await sessions.EndAsync(session.Id, command.UserId, now, cancellationToken).ConfigureAwait(false);
            return access.Error;
        }

        if (!await sessions.TryHeartbeatAsync(session.Id, command.UserId, now, cancellationToken).ConfigureAwait(false))
        {
            return PlaybackErrors.SessionEnded;
        }

        await positions.RecordAsync(session, command.PositionSeconds, now, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
