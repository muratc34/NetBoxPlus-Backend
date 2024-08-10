using Domain.Core.Errors;
using Shared.Domain.Result;

namespace Auth.Domain.Users;

public sealed record FirstName
{
    public const int MaxLength = 100;
    private FirstName(string value) => Value = value;

    public string Value { get; set; }

    public static Result<FirstName> Create(string firstName)
    {
        if(string.IsNullOrWhiteSpace(firstName))
        { 
            return Result.Failure<FirstName>(DomainErrors.FirstName.NullOrEmpty);
        }

        if (firstName.Length > MaxLength)
        {
            return Result.Failure<FirstName>(DomainErrors.FirstName.LongerThanAllowed);
        }
        return new FirstName(firstName);
    }
}
