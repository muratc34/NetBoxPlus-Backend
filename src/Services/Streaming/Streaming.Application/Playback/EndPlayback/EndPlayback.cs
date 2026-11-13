using Shared.Application.Cqrs;
using Shared.Domain;
using Streaming.Domain.Playback;

namespace Streaming.Application.Playback.EndPlayback;

/// <summary>The player stopped; its screen is free at once. Ending an ended session again is a success.</summary>
public sealed record EndPlaybackCommand(Guid UserId, Guid SessionId) : ICommand;

internal sealed class EndPlaybackHandler(IPlaybackSessionRepository sessions, TimeProvider timeProvider) : ICommandHandler<EndPlaybackCommand>
{
    public async Task<Result> Handle(EndPlaybackCommand command, CancellationToken cancellationToken)
        => await sessions.EndAsync(command.SessionId, command.UserId, timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false)
            ? Result.Success()
            : PlaybackErrors.SessionEnded;
}
