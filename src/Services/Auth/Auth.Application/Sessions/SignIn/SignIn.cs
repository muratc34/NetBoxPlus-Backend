using Auth.Application.Abstractions;
using Auth.Domain.RefreshTokens;
using Auth.Domain.Roles;
using Auth.Domain.Users;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Auth.Application.Sessions.SignIn;

/// <param name="Kind">An admin session also requires <see cref="Permissions.Auth.AdminSignIn"/>.</param>
public sealed record SignInCommand(string Email, string Password, SessionKind Kind) : ICommand<TokenPair>;

internal sealed class SignInValidator : IValidator<SignInCommand>
{
    // Presence and an upper bound only: the password policy applies to new passwords, and the
    // bound keeps oversized input away from the hasher.
    public IEnumerable<ValidationFailure> Validate(SignInCommand instance)
    {
        if (string.IsNullOrWhiteSpace(instance.Email))
        {
            yield return Failure(nameof(instance.Email), UserErrors.EmailRequired);
        }
        else if (instance.Email.Length > EmailAddress.MaxLength)
        {
            yield return Failure(nameof(instance.Email), UserErrors.EmailTooLong);
        }

        if (string.IsNullOrEmpty(instance.Password))
        {
            yield return Failure(nameof(instance.Password), UserErrors.PasswordRequired);
        }
        else if (instance.Password.Length > PasswordPolicy.MaxLength)
        {
            yield return Failure(nameof(instance.Password), UserErrors.PasswordTooLong);
        }
    }

    private static ValidationFailure Failure(string property, Error error) => new(property, error.Code, error.Message);
}

/// <summary>
/// Every failure is <see cref="UserErrors.InvalidCredentials"/>, and every path verifies one
/// password hash, so neither the response nor its timing tells whether the account exists.
/// </summary>
internal sealed class SignInHandler(
    IUserRepository users,
    IPasswordHasher hasher,
    UnknownUserPassword unknownUserPassword,
    SessionTokens sessions,
    TimeProvider timeProvider) : ICommandHandler<SignInCommand, TokenPair>
{
    public async Task<Result<TokenPair>> Handle(SignInCommand command, CancellationToken cancellationToken)
    {
        var email = EmailAddress.Create(command.Email);
        var user = email.IsSuccess
            ? await users.GetByEmailAsync(email.Value, cancellationToken).ConfigureAwait(false)
            : null;

        if (user is null)
        {
            unknownUserPassword.Verify(command.Password);
            return UserErrors.InvalidCredentials;
        }

        var now = timeProvider.GetUtcNow();
        var check = hasher.Verify(user.PasswordHash, command.Password);

        // A locked or disabled account answers like a wrong password, and tries during a lockout
        // are not counted: the lockout already stops them.
        if (user.CanSignIn(now).IsFailure)
        {
            return UserErrors.InvalidCredentials;
        }

        if (check == PasswordCheck.Failed)
        {
            await users.RecordFailedSignInAsync(user.Id, now, cancellationToken).ConfigureAwait(false);
            return UserErrors.InvalidCredentials;
        }

        var grants = await sessions.GetGrantsAsync(user, cancellationToken).ConfigureAwait(false);
        if (command.Kind == SessionKind.Admin && !grants.Has(Permissions.Auth.AdminSignIn))
        {
            return UserErrors.InvalidCredentials;
        }

        user.RecordSuccessfulSignIn();
        if (check == PasswordCheck.SucceededRehashNeeded)
        {
            user.ChangePasswordHash(hasher.Hash(command.Password));
        }

        return sessions.Start(user, command.Kind, grants);
    }
}
