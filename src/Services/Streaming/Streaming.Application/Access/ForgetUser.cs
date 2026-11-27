using Auth.Contracts;
using Shared.Application.Cqrs;
using Shared.Application.Messaging;
using Shared.Domain;
using Streaming.Domain.Access;
using Streaming.Domain.Lists;
using Streaming.Domain.Playback;
using Streaming.Domain.Ratings;

namespace Streaming.Application.Access;

/// <summary>
/// The user's account was deleted: their profiles are removed for good (a profile event arriving late
/// cannot bring one back), what they watched, saved and rated is forgotten, and their sessions end at once. The
/// subscription copy follows billing's cancellation like any other. Coming again changes nothing.
/// </summary>
public sealed record ForgetUserCommand(Guid UserId) : ICommand;

internal sealed class ForgetUserHandler(
    IProfileCopyRepository profiles,
    IWatchProgressRepository progress,
    IMyListRepository lists,
    IPlaybackPreferenceRepository preferences,
    ITitleRatingRepository ratings,
    IViewerRepository viewers,
    TimeProvider timeProvider) : ICommandHandler<ForgetUserCommand>
{
    public async Task<Result> Handle(ForgetUserCommand command, CancellationToken cancellationToken)
    {
        foreach (var profile in await profiles.GetByUserIdAsync(command.UserId, cancellationToken).ConfigureAwait(false))
        {
            profile.RemoveForGood();
        }

        await progress.RemoveForUserAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        await lists.RemoveForUserAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        await preferences.RemoveForUserAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        await ratings.RemoveForUserAsync(command.UserId, cancellationToken).ConfigureAwait(false);

        if (await viewers.GetAsync(command.UserId, cancellationToken).ConfigureAwait(false) is { } viewer)
        {
            viewer.EndAll(timeProvider.GetUtcNow());
        }

        return Result.Success();
    }
}

/// <summary>
/// Deleted accounts, from the account service. The full type name is this handler's inbox key:
/// renaming or moving it would process the messages it already handled again.
/// </summary>
internal sealed class ForgetUserOnUserDeleted(IDispatcher dispatcher) : IIntegrationEventHandler<UserDeleted>
{
    public Task<Result> Handle(UserDeleted integrationEvent, CancellationToken cancellationToken)
        => dispatcher.Send(new ForgetUserCommand(integrationEvent.UserId), cancellationToken);
}
