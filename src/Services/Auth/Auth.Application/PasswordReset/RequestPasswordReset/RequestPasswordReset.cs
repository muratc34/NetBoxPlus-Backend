using Auth.Application.UserTokens;
using Auth.Domain.Users;
using Auth.Domain.UserTokens;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Auth.Application.PasswordReset.RequestPasswordReset;

public sealed record RequestPasswordResetCommand(string Email) : ICommand;

internal sealed class RequestPasswordResetValidator : IValidator<RequestPasswordResetCommand>
{
    public IEnumerable<ValidationFailure> Validate(RequestPasswordResetCommand instance)
    {
        var email = EmailAddress.Create(instance.Email);
        if (email.IsFailure)
        {
            yield return new ValidationFailure(nameof(instance.Email), email.Error.Code, email.Error.Message);
        }
    }
}

/// <summary>
/// E-mails a password reset link to the address if it has an account. Succeeds the same way when it
/// has none, when the account is disabled and when a link was sent less than a minute ago, so the
/// answer does not tell whether an account exists; only the e-mail does, and it goes to the owner.
/// The links sent before stop working. A locked account may reset: the reset lifts the lockout.
/// </summary>
internal sealed class RequestPasswordResetHandler(
    IUserRepository users,
    IUserTokenRepository tokens,
    UserTokenIssuer tokenIssuer,
    TimeProvider timeProvider) : ICommandHandler<RequestPasswordResetCommand>
{
    public async Task<Result> Handle(RequestPasswordResetCommand command, CancellationToken cancellationToken)
    {
        var email = EmailAddress.Create(command.Email).Value;
        var user = await users.GetByEmailAsync(email, cancellationToken).ConfigureAwait(false);
        if (user is null || user.IsDisabled)
        {
            return Result.Success();
        }

        var now = timeProvider.GetUtcNow();
        var lastIssuedAt = await tokens.GetLastIssuedAtAsync(user.Id, UserTokenPurpose.PasswordReset, cancellationToken).ConfigureAwait(false);
        if (lastIssuedAt > now - UserToken.PasswordResetCooldown)
        {
            return Result.Success();
        }

        // Tracked, not ExecuteUpdate: if the command fails, the old links keep working.
        foreach (var previous in await tokens.GetUnusedAsync(user.Id, UserTokenPurpose.PasswordReset, cancellationToken).ConfigureAwait(false))
        {
            previous.Revoke(now);
        }

        tokenIssuer.IssuePasswordReset(user.Id);
        return Result.Success();
    }
}
