using Shared.Application.Abstractions;
using Shared.Domain.Result;

namespace Auth.Application.Profiles.SetProfilePin;

public sealed class SetProfilePinCommand: ICommand<Result>
{
    public SetProfilePinCommand(Guid profileId, string pin)
    {
        ProfileId = profileId;
        Pin = pin;
    }
    public Guid ProfileId { get; set; }
    public string Pin { get; set; }
}
