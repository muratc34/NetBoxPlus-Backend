using Shared.Application.Abstractions;
using Shared.Domain.Result;

namespace Auth.Application.Profiles.CreateProfile;

public sealed class CreateProfileCommand : ICommand<Result>
{
    public CreateProfileCommand(string profileName)
    {
        ProfileName = profileName;
    }

    public string ProfileName { get; }
}
