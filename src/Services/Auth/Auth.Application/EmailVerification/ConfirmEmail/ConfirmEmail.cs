using Auth.Domain.Users;
using Auth.Domain.UserTokens;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Auth.Application.EmailVerification.ConfirmEmail;

/// <param name="Token">The value from the e-mailed link.</param>
public sealed record ConfirmEmailCommand(string? Token) : ICommand;

/// <summary>
/// Verifies the address the token was sent to. Needs no sign-in: the token is the proof. Every
/// failure, a malformed token included, is the same error, so a caller learns nothing about tokens
/// it does not hold. (No validator for that reason: it would answer with a different code.)
/// </summary>
internal sealed class ConfirmEmailHandler(IUserTokenRepository tokens, IUserRepository users, TimeProvider timeProvider)
    : ICommandHandler<ConfirmEmailCommand>
{
    /// <summary>Far above a real token (43 characters); longer input is not worth hashing.</summary>
    public const int MaxTokenLength = 256;

    public async Task<Result> Handle(ConfirmEmailCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Token) || command.Token.Length > MaxTokenLength)
        {
            return UserTokenErrors.EmailVerificationInvalid;
        }

        var now = timeProvider.GetUtcNow();
        var token = await tokens.GetByHashAsync(EmailVerificationTokens.Hash(command.Token), cancellationToken).ConfigureAwait(false);

        if (token is null || token.Purpose != UserTokenPurpose.EmailVerification || !token.IsActive(now))
        {
            return UserTokenErrors.EmailVerificationInvalid;
        }

        if (!await tokens.TryMarkUsedAsync(token.Id, now, cancellationToken).ConfigureAwait(false))
        {
            return UserTokenErrors.EmailVerificationInvalid;
        }

        var user = await users.GetByIdAsync(token.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return UserTokenErrors.EmailVerificationInvalid;
        }

        user.VerifyEmail(now);
        return Result.Success();
    }
}
