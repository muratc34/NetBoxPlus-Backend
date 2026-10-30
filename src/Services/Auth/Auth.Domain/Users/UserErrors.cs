using Shared.Domain;

namespace Auth.Domain.Users;

public static class UserErrors
{
    public static readonly Error EmailRequired = Error.Validation("User.EmailRequired", "The e-mail address is required.");

    public static readonly Error EmailTooLong = Error.Validation(
        "User.EmailTooLong",
        $"The e-mail address must be at most {EmailAddress.MaxLength} characters.");

    public static readonly Error EmailInvalid = Error.Validation("User.EmailInvalid", "The e-mail address is not valid.");

    public static readonly Error PasswordRequired = Error.Validation("User.PasswordRequired", "The password is required.");

    public static readonly Error PasswordTooShort = Error.Validation(
        "User.PasswordTooShort",
        $"The password must be at least {PasswordPolicy.MinLength} characters.");

    public static readonly Error PasswordTooLong = Error.Validation(
        "User.PasswordTooLong",
        $"The password must be at most {PasswordPolicy.MaxLength} characters.");

    public static readonly Error Disabled = Error.Forbidden("User.Disabled", "The account is disabled.");

    public static readonly Error LockedOut = Error.Forbidden(
        "User.LockedOut",
        "The account is temporarily locked after too many failed sign-in attempts.");

    public static readonly Error NotFound = Error.NotFound("User.NotFound", "The user was not found.");
}
