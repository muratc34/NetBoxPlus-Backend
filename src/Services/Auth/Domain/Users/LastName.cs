using Domain.Core.Errors;
using Shared.Domain.Result;

namespace Auth.Domain.Users;

public sealed record LastName
{
    public const int MaxLength = 100;

    private LastName(string value) => Value = value;

    public string Value { get; set; }

    public static Result<LastName> Create(string lastName)
    {
        if (string.IsNullOrWhiteSpace(lastName))
        {
            return Result.Failure<LastName>(DomainErrors.FirstName.NullOrEmpty);
        }

        if (lastName.Length > MaxLength)
        {
            return Result.Failure<LastName>(DomainErrors.FirstName.LongerThanAllowed);
        }
        return new LastName(lastName);
    }
}
