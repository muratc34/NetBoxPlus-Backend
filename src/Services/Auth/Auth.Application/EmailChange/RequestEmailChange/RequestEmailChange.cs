using Auth.Application.Abstractions;
using Auth.Application.UserTokens;
using Auth.Domain.Users;
using Auth.Domain.UserTokens;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Auth.Application.EmailChange.RequestEmailChange;

/// <param name="UserId">The signed-in caller, from the access token.</param>
public sealed record RequestEmailChangeCommand(Guid UserId, string? Password, string? NewEmail) : ICommand;

internal sealed class RequestEmailChangeValidator : IValidator<RequestEmailChangeCommand>
{
    public IEnumerable<ValidationFailure> Validate(RequestEmailChangeCommand instance)
    {
        if (string.IsNullOrEmpty(instance.Password))
        {
            yield return new ValidationFailure(nameof(instance.Password), UserErrors.PasswordRequired.Code, UserErrors.PasswordRequired.Message);
        }
        else if (instance.Password.Length > PasswordPolicy.MaxLength)
        {
            yield return new ValidationFailure(nameof(instance.Password), UserErrors.PasswordTooLong.Code, UserErrors.PasswordTooLong.Message);
        }

        var email = EmailAddress.Create(instance.NewEmail);
        if (email.IsFailure)
        {
            yield return new ValidationFailure(nameof(instance.NewEmail), email.Error.Code, email.Error.Message);
        }
    }
}

/// <summary>
/// Sends a link to the new address; the account moves there only when the link is followed. The
/// password is checked first and counts as a sign-in attempt, so a stolen access token alone cannot
/// take the account to another mailbox, and only then whether the address is free. Earlier change
/// links stop working, and a new one can be asked for once a minute.
/// </summary>
internal sealed class RequestEmailChangeHandler(
    IUserRepository users,
    IUserTokenRepository tokens,
    UserTokenIssuer tokenIssuer,
    IPasswordHasher hasher,
    TimeProvider timeProvider) : ICommandHandler<RequestEmailChangeCommand>
{
    public async Task<Result> Handle(RequestEmailChangeCommand command, CancellationToken cancellationToken)
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

        var email = EmailAddress.Create(command.NewEmail).Value;
        if (email == user.Email)
        {
            return UserErrors.EmailUnchanged;
        }

        if (await users.ExistsByEmailAsync(email, cancellationToken).ConfigureAwait(false))
        {
            return UserErrors.EmailTaken;
        }

        var lastIssuedAt = await tokens.GetLastIssuedAtAsync(user.Id, UserTokenPurpose.EmailChange, cancellationToken).ConfigureAwait(false);
        if (lastIssuedAt > now - UserToken.EmailChangeCooldown)
        {
            return UserTokenErrors.EmailChangeRequestedRecently;
        }

        // Tracked, not ExecuteUpdate: if the command fails, the earlier link keeps working.
        foreach (var previous in await tokens.GetUnusedAsync(user.Id, UserTokenPurpose.EmailChange, cancellationToken).ConfigureAwait(false))
        {
            previous.Revoke(now);
        }

        tokenIssuer.IssueEmailChange(user.Id, email.Value);
        return Result.Success();
    }
}
