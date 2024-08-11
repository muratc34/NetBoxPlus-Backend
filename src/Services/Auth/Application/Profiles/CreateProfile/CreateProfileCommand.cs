using Shared.Application.Abstractions;
using Shared.Domain.Result;

namespace Auth.Application.Profiles.CreateProfile;

public sealed class CreateProfileCommand : ICommand<Result>
{
    public CreateProfileCommand(string profileName, string? pin)
    {
        ProfileName = profileName;
        Pin = pin;
    }

    public string ProfileName { get; }
    public string? Pin { get; }
}
