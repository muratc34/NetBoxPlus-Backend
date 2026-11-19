using Auth.Domain.Users;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Auth.Application.Profiles.ManageProfiles;

/// <summary>Adds a profile to the caller's account.</summary>
public sealed record CreateProfileCommand(Guid UserId, string? Name, AgeRating MaxAgeRating) : ICommand<Guid>;

/// <summary>Renames a profile of the caller's account or changes its age rating.</summary>
public sealed record UpdateProfileCommand(Guid UserId, Guid ProfileId, string? Name, AgeRating MaxAgeRating) : ICommand;

/// <summary>Removes a profile of the caller's account; the last one stays.</summary>
public sealed record RemoveProfileCommand(Guid UserId, Guid ProfileId) : ICommand;

internal sealed class CreateProfileHandler(IUserRepository users) : ICommandHandler<CreateProfileCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateProfileCommand command, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        var profile = user.AddProfile(command.Name, command.MaxAgeRating);
        return profile.IsFailure ? profile.Error : profile.Value.Id;
    }
}

internal sealed class UpdateProfileHandler(IUserRepository users) : ICommandHandler<UpdateProfileCommand>
{
    public async Task<Result> Handle(UpdateProfileCommand command, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        return user is null ? UserErrors.NotFound : user.ChangeProfile(command.ProfileId, command.Name, command.MaxAgeRating);
    }
}

internal sealed class RemoveProfileHandler(IUserRepository users) : ICommandHandler<RemoveProfileCommand>
{
    public async Task<Result> Handle(RemoveProfileCommand command, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        return user is null ? UserErrors.NotFound : user.RemoveProfile(command.ProfileId);
    }
}
