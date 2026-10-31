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

    public static readonly Error EmailTaken = Error.Conflict("User.EmailTaken", "An account with this e-mail address already exists.");

    /// <summary>
    /// The only sign-in failure a caller sees: an unknown e-mail, a wrong password, a locked or a
    /// disabled account all look the same, so the response does not tell whether an account exists.
    /// </summary>
    public static readonly Error InvalidCredentials = Error.Unauthorized(
        "User.InvalidCredentials",
        "The e-mail address or password is incorrect, or the account is temporarily locked.");

    public static readonly Error PasswordIncorrect = Error.Validation("User.PasswordIncorrect", "The current password is incorrect.");

    public static readonly Error CannotModifySelf = Error.Conflict(
        "User.CannotModifySelf",
        "You cannot disable your own account or remove your own roles.");
}
