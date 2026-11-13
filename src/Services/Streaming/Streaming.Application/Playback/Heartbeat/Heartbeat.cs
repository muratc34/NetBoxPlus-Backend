using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Domain;
using Streaming.Application.Abstractions;
using Streaming.Domain.Playback;

namespace Streaming.Application.Playback.Heartbeat;

/// <summary>
/// The player is still watching. The rules are checked again: a subscription that stopped, a title
/// taken down or a profile removed ends the session.
/// </summary>
public sealed record HeartbeatCommand(Guid UserId, Guid SessionId) : ICommand;

internal sealed class HeartbeatHandler(IStreamingReadModel readModel, PlaybackGate gate, IPlaybackSessionRepository sessions, TimeProvider timeProvider)
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

        return await sessions.TryHeartbeatAsync(session.Id, command.UserId, now, cancellationToken).ConfigureAwait(false)
            ? Result.Success()
            : PlaybackErrors.SessionEnded;
    }
}
