using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;
using Streaming.Application.Abstractions;
using Streaming.Domain.Playback;

namespace Streaming.Application.Playback.EndPlayback;

/// <summary>
/// The player stopped, at <paramref name="PositionSeconds"/> when it says so; its screen is free at
/// once. Ending an ended session again is a success, and its position is no longer taken.
/// </summary>
public sealed record EndPlaybackCommand(Guid UserId, Guid SessionId, double? PositionSeconds = null) : ICommand;

internal sealed class EndPlaybackValidator : IValidator<EndPlaybackCommand>
{
    public IEnumerable<ValidationFailure> Validate(EndPlaybackCommand instance)
        => PlaybackPositions.Check(instance.PositionSeconds);
}

internal sealed class EndPlaybackHandler(IStreamingReadModel readModel, IPlaybackSessionRepository sessions, PlaybackPositions positions, TimeProvider timeProvider)
    : ICommandHandler<EndPlaybackCommand>
{
    public async Task<Result> Handle(EndPlaybackCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var session = await readModel.PlaybackSessions
            .FirstOrDefaultAsync(item => item.Id == command.SessionId && item.UserId == command.UserId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return PlaybackErrors.SessionEnded;
        }

        if (session.IsLive(now))
        {
            await positions.RecordAsync(session, command.PositionSeconds, now, cancellationToken).ConfigureAwait(false);
        }

        await sessions.EndAsync(session.Id, command.UserId, now, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
