using Auth.Application.UserTokens;
using Auth.Domain.Users;
using Auth.Domain.UserTokens;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Auth.Application.EmailChange.ConfirmEmailChange;

/// <param name="Token">The value from the e-mailed link.</param>
public sealed record ConfirmEmailChangeCommand(string? Token) : ICommand;

/// <summary>
/// Moves the account to the address the token was sent to. Needs no sign-in: the token proves the
/// user owns that mailbox, and the password was checked when the link was asked for. Every token
/// failure, a malformed token included, is the same error (no validator for that reason). The
/// account's other unused links stop working: they went to the old address.
/// </summary>
internal sealed class ConfirmEmailChangeHandler(IUserTokenRepository tokens, IUserRepository users, TimeProvider timeProvider)
    : ICommandHandler<ConfirmEmailChangeCommand>
{
    private static readonly UserTokenPurpose[] OldAddressPurposes =
        [UserTokenPurpose.EmailVerification, UserTokenPurpose.PasswordReset, UserTokenPurpose.EmailChange];

    public async Task<Result> Handle(ConfirmEmailChangeCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Token) || command.Token.Length > UserTokenIssuer.MaxValueLength)
        {
            return UserTokenErrors.EmailChangeInvalid;
        }

        var now = timeProvider.GetUtcNow();
        var token = await tokens.GetByHashAsync(UserTokenIssuer.Hash(command.Token), cancellationToken).ConfigureAwait(false);

        if (token is null || token.Purpose != UserTokenPurpose.EmailChange || !token.IsActive(now) || token.Email is null)
        {
            return UserTokenErrors.EmailChangeInvalid;
        }

        var user = await users.GetByIdAsync(token.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null || user.IsDisabled)
        {
            return UserTokenErrors.EmailChangeInvalid;
        }

        if (!await tokens.TryMarkUsedAsync(token.Id, now, cancellationToken).ConfigureAwait(false))
        {
            return UserTokenErrors.EmailChangeInvalid;
        }

        // Taken since the link was sent. The token stays used: the user asks again with another address.
        // A registration racing this one is stopped by the unique index (409 Data.Conflict).
        var email = EmailAddress.Create(token.Email).Value;
        if (email != user.Email && await users.ExistsByEmailAsync(email, cancellationToken).ConfigureAwait(false))
        {
            return UserErrors.EmailTaken;
        }

        user.ChangeEmail(email, now);

        foreach (var purpose in OldAddressPurposes)
        {
            foreach (var other in await tokens.GetUnusedAsync(user.Id, purpose, cancellationToken).ConfigureAwait(false))
            {
                other.Revoke(now);
            }
        }

        return Result.Success();
    }
}
