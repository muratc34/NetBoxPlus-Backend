using Auth.Application.Abstractions;
using Auth.Application.Sessions;
using Auth.Domain.RefreshTokens;
using Auth.Domain.Users;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Auth.Application.Users.ChangePassword;

/// <param name="UserId">The signed-in caller, from the access token.</param>
public sealed record ChangePasswordCommand(Guid UserId, string CurrentPassword, string NewPassword) : ICommand<TokenPair>;

internal sealed class ChangePasswordValidator : IValidator<ChangePasswordCommand>
{
    public IEnumerable<ValidationFailure> Validate(ChangePasswordCommand instance)
    {
        if (string.IsNullOrEmpty(instance.CurrentPassword))
        {
            yield return new ValidationFailure(nameof(instance.CurrentPassword), UserErrors.PasswordRequired.Code, UserErrors.PasswordRequired.Message);
        }
        else if (instance.CurrentPassword.Length > PasswordPolicy.MaxLength)
        {
            yield return new ValidationFailure(nameof(instance.CurrentPassword), UserErrors.PasswordTooLong.Code, UserErrors.PasswordTooLong.Message);
        }

        var password = PasswordPolicy.Check(instance.NewPassword);
        if (password.IsFailure)
        {
            yield return new ValidationFailure(nameof(instance.NewPassword), password.Error.Code, password.Error.Message);
        }
    }
}

/// <summary>
/// Changes the password after checking the current one, which counts as a sign-in attempt: a stolen
/// access token alone is not enough to guess it. Every session of the user ends; the caller gets a
/// new pair to continue with. Access tokens already issued stay valid until they expire.
/// </summary>
internal sealed class ChangePasswordHandler(
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IPasswordHasher hasher,
    SessionTokens sessions,
    TimeProvider timeProvider) : ICommandHandler<ChangePasswordCommand, TokenPair>
{
    public async Task<Result<TokenPair>> Handle(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        var now = timeProvider.GetUtcNow();
        var check = hasher.Verify(user.PasswordHash, command.CurrentPassword);

        if (user.CanSignIn(now).IsFailure)
        {
            return UserErrors.PasswordIncorrect;
        }

        if (check == PasswordCheck.Failed)
        {
            await users.RecordFailedSignInAsync(user.Id, now, cancellationToken).ConfigureAwait(false);
            return UserErrors.PasswordIncorrect;
        }

        user.ChangePasswordHash(hasher.Hash(command.NewPassword));
        user.RecordSuccessfulSignIn();

        // At once, like every revocation: the new pair below is added afterwards and saved with the command.
        await refreshTokens.RevokeAllForUserAsync(user.Id, now, cancellationToken).ConfigureAwait(false);

        var grants = await sessions.GetGrantsAsync(user, cancellationToken).ConfigureAwait(false);
        return sessions.Start(user, SessionKind.Customer, grants);
    }
}
