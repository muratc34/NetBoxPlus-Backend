using Auth.Application.Abstractions;
using Auth.Domain.Users;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Auth.Application.Users.DeleteAccount;

/// <param name="UserId">The signed-in caller, from the access token.</param>
public sealed record DeleteAccountCommand(Guid UserId, string? Password) : ICommand;

internal sealed class DeleteAccountValidator : IValidator<DeleteAccountCommand>
{
    public IEnumerable<ValidationFailure> Validate(DeleteAccountCommand instance)
    {
        if (string.IsNullOrEmpty(instance.Password))
        {
            yield return new ValidationFailure(nameof(instance.Password), UserErrors.PasswordRequired.Code, UserErrors.PasswordRequired.Message);
        }
        else if (instance.Password.Length > PasswordPolicy.MaxLength)
        {
            yield return new ValidationFailure(nameof(instance.Password), UserErrors.PasswordTooLong.Code, UserErrors.PasswordTooLong.Message);
        }
    }
}

/// <summary>
/// The user deletes their own account, confirming it with the password, which counts as a sign-in
/// attempt: a stolen access token alone cannot delete an account. There is no undo.
/// </summary>
internal sealed class DeleteAccountHandler(
    IUserRepository users,
    IPasswordHasher hasher,
    AccountDeletion deletion,
    TimeProvider timeProvider) : ICommandHandler<DeleteAccountCommand>
{
    public async Task<Result> Handle(DeleteAccountCommand command, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        var now = timeProvider.GetUtcNow();
        var check = hasher.Verify(user.PasswordHash, command.Password!);

        if (user.CanSignIn(now).IsFailure)
        {
            return UserErrors.PasswordIncorrect;
        }

        if (check == PasswordCheck.Failed)
        {
            await users.RecordFailedSignInAsync(user.Id, now, cancellationToken).ConfigureAwait(false);
            return UserErrors.PasswordIncorrect;
        }

        await deletion.DeleteAsync(user, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
