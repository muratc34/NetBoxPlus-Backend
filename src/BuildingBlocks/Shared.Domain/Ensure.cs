using System.Runtime.CompilerServices;

namespace Shared.Domain;

/// <summary>
/// Guards for programming errors that the built-in <c>ThrowIf*</c> helpers do not cover. These
/// throw; invalid user input is reported with <see cref="Result"/> instead.
/// </summary>
public static class Ensure
{
    public static Guid NotEmpty(Guid value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("The value cannot be an empty GUID.", paramName);
        }

        return value;
    }

    public static T NotDefault<T>(T value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : struct, IEquatable<T>
    {
        if (value.Equals(default))
        {
            throw new ArgumentException("The value cannot be the default value.", paramName);
        }

        return value;
    }
}
