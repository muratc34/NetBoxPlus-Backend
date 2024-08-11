using Domain.Core.Errors;
using Shared.Domain.Result;

namespace Auth.Domain.Profiles;

public sealed record ProfileName
{
    public const int MaxLength = 100;
    public ProfileName(string value) => Value = value;

    public string Value { get; set; }

    public static Result<ProfileName> Create(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return Result.Failure<ProfileName>(DomainErrors.ProfileName.NullOrEmpty);
        }

        if (profileName.Length > MaxLength)
        {
            return Result.Failure<ProfileName>(DomainErrors.ProfileName.LongerThanAllowed);
        }
        return new ProfileName(profileName);
    }
}
