using Shared.Application.Cqrs;
using Shared.Domain;
using Streaming.Domain.Access;
using Streaming.Domain.Playback;

namespace Streaming.Application.Access;

/// <summary>The account service's news about a profile; no age rating means it was removed. Sent by the event handlers, not by people.</summary>
public sealed record ApplyProfileCommand(Guid ProfileId, Guid UserId, AgeRating? MaxAgeRating, int Revision) : ICommand;

internal sealed class ApplyProfileHandler(IProfileCopyRepository profiles, IWatchProgressRepository progress) : ICommandHandler<ApplyProfileCommand>
{
    public async Task<Result> Handle(ApplyProfileCommand command, CancellationToken cancellationToken)
    {
        var profile = await profiles.GetByIdAsync(command.ProfileId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            profile = ProfileCopy.Create(command.ProfileId, command.UserId);
            profiles.Add(profile);
        }

        if (command.MaxAgeRating is { } maxAgeRating)
        {
            profile.Apply(maxAgeRating, command.Revision);
        }
        else if (profile.Remove(command.Revision))
        {
            // What a removed profile watched goes with it.
            await progress.RemoveForProfileAsync(command.ProfileId, cancellationToken).ConfigureAwait(false);
        }

        return Result.Success();
    }
}
