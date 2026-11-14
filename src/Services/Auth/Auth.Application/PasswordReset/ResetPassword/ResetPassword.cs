using Auth.Application.Abstractions;
using Auth.Application.UserTokens;
using Auth.Domain.RefreshTokens;
using Auth.Domain.Users;
using Auth.Domain.UserTokens;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Auth.Application.PasswordReset.ResetPassword;

/// <param name="Token">The value from the e-mailed link.</param>
public sealed record ResetPasswordCommand(string? Token, string NewPassword) : ICommand;

/// <summary>
/// Checks only the new password, which says nothing about the token. The token is checked by the
/// handler, where every failure is the same error.
/// </summary>
internal sealed class ResetPasswordValidator : IValidator<ResetPasswordCommand>
{
    public IEnumerable<ValidationFailure> Validate(ResetPasswordCommand instance)
    {
        var password = PasswordPolicy.Check(instance.NewPassword);
        if (password.IsFailure)
        {
            yield return new ValidationFailure(nameof(instance.NewPassword), password.Error.Code, password.Error.Message);
        }
    }
}

/// <summary>
/// Sets a new password with the token from a reset link. Needs no sign-in: the token is the proof.
/// Every session of the user ends and the user signs in again with the new password; access tokens
/// already issued stay valid until they expire. A missing, malformed, unknown, expired, used or
/// replaced token, and a disabled account, are all the same error.
/// </summary>
internal sealed class ResetPasswordHandler(
    IUserTokenRepository tokens,
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IPasswordHasher hasher,
    TimeProvider timeProvider) : ICommandHandler<ResetPasswordCommand>
{
    public async Task<Result> Handle(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Token) || command.Token.Length > UserTokenIssuer.MaxValueLength)
        {
            return UserTokenErrors.PasswordResetInvalid;
        }

        var now = timeProvider.GetUtcNow();
        var token = await tokens.GetByHashAsync(UserTokenIssuer.Hash(command.Token), cancellationToken).ConfigureAwait(false);

        if (token is null || token.Purpose != UserTokenPurpose.PasswordReset || !token.IsActive(now))
        {
            return UserTokenErrors.PasswordResetInvalid;
        }

        var user = await users.GetByIdAsync(token.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null || user.IsDisabled)
        {
            return UserTokenErrors.PasswordResetInvalid;
        }

        if (!await tokens.TryMarkUsedAsync(token.Id, now, cancellationToken).ConfigureAwait(false))
        {
            return UserTokenErrors.PasswordResetInvalid;
        }

        user.ResetPassword(hasher.Hash(command.NewPassword), now);

        // The other links of the account stop working with this one; saved with the command.
        foreach (var other in await tokens.GetUnusedAsync(user.Id, UserTokenPurpose.PasswordReset, cancellationToken).ConfigureAwait(false))
        {
            other.Revoke(now);
        }

        // At once, like every revocation: whoever held a session with the old password loses it.
        await refreshTokens.RevokeAllForUserAsync(user.Id, now, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
