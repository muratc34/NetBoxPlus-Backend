using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;
using Streaming.Application.Abstractions;
using Streaming.Domain.Playback;

namespace Streaming.Application.Playback.StartPlayback;

/// <summary>Starts watching a movie or an episode on one screen.</summary>
public sealed record StartPlaybackCommand(Guid UserId, Guid ProfileId, Guid ContentId) : ICommand<PlaybackStarted>;

/// <param name="Playlist">
/// The master playlist, relative to the address the session was started at; valid for a few minutes.
/// </param>
/// <param name="HeartbeatSeconds">How often the player must report that it is still watching.</param>
/// <param name="ResumeFromSeconds">Where the profile stopped last time; 0 when new or watched to the end.</param>
public sealed record PlaybackStarted(Guid SessionId, string Playlist, int HeartbeatSeconds, double ResumeFromSeconds);

internal sealed class StartPlaybackValidator : IValidator<StartPlaybackCommand>
{
    private static readonly Error ProfileRequired = Error.Validation("Playback.ProfileRequired", "The profile id is required.");

    private static readonly Error ContentRequired = Error.Validation("Playback.ContentRequired", "The id of the movie or episode is required.");

    public IEnumerable<ValidationFailure> Validate(StartPlaybackCommand instance)
    {
        if (instance.ProfileId == Guid.Empty)
        {
            yield return new ValidationFailure(nameof(instance.ProfileId), ProfileRequired.Code, ProfileRequired.Message);
        }

        if (instance.ContentId == Guid.Empty)
        {
            yield return new ValidationFailure(nameof(instance.ContentId), ContentRequired.Code, ContentRequired.Message);
        }
    }
}

internal sealed class StartPlaybackHandler(PlaybackGate gate, IViewerRepository viewers, IWatchProgressRepository progress, IPlaybackSigner signer, TimeProvider timeProvider)
    : ICommandHandler<StartPlaybackCommand, PlaybackStarted>
{
    public async Task<Result<PlaybackStarted>> Handle(StartPlaybackCommand command, CancellationToken cancellationToken)
    {
        var access = await gate.CheckAsync(command.UserId, command.ProfileId, command.ContentId, cancellationToken).ConfigureAwait(false);
        if (access.IsFailure)
        {
            return access.Error;
        }

        var viewer = await viewers.GetAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (viewer is null)
        {
            // Two first sessions at once: the primary key lets one through, the other gets a conflict.
            viewer = Viewer.Create(command.UserId);
            viewers.Add(viewer);
        }

        var now = timeProvider.GetUtcNow();
        var started = viewer.Start(access.Value.Grant, access.Value.MaxScreens, now);
        if (started.IsFailure)
        {
            return started.Error;
        }

        var session = started.Value;
        var resume = await progress.FindAsync(command.ProfileId, command.ContentId, cancellationToken).ConfigureAwait(false);
        var query = signer.Sign(session.Id, PlaybackLinks.MasterPlaylist, now + PlaybackLinks.MasterLifetime);
        return new PlaybackStarted(
            session.Id,
            $"playback/{session.Id}/{PlaybackLinks.MasterPlaylist}?{query}",
            (int)PlaybackSession.HeartbeatInterval.TotalSeconds,
            resume?.ResumeFromSeconds ?? 0);
    }
}
