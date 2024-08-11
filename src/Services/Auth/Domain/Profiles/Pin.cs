using Domain.Core.Errors;
using Shared.Domain.Result;

namespace Auth.Domain.Profiles;

public sealed record Pin
{
    private const int PinLength = 4;
    private static readonly Func<char, bool> IsDigit = c => c >= '0' && c <= '9';

    public Pin(string value) => Value = value;

    public string Value { get; }

    public static Result<Pin> Create(string pin)
    {

        if (pin.Length != PinLength)
        {
            return Result.Failure<Pin>(DomainErrors.Pin.MustBe4Digits);
        }
        if (!pin.Any(IsDigit))
        {
            return Result.Failure<Pin>(DomainErrors.Pin.ConsistOfNums);
        }

        return new Pin(pin);
    }
}
