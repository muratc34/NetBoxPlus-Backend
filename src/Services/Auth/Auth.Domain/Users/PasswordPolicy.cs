using Shared.Domain;

namespace Auth.Domain.Users;

/// <summary>
/// Rules a new password must meet. Length only, following NIST SP 800-63B: no composition rules
/// (they push people toward predictable patterns), a generous maximum so passphrases fit.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 12;
    public const int MaxLength = 128;

    public static Result Check(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return UserErrors.PasswordRequired;
        }

        if (password.Length < MinLength)
        {
            return UserErrors.PasswordTooShort;
        }

        if (password.Length > MaxLength)
        {
            return UserErrors.PasswordTooLong;
        }

        return Result.Success();
    }
}
