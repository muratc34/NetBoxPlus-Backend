using Auth.Domain.Users;
using Auth.Domain.UserTokens;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Auth.Application.EmailVerification.ResendVerification;

/// <param name="UserId">The signed-in caller, from the access token.</param>
public sealed record ResendVerificationCommand(Guid UserId) : ICommand;

/// <summary>
/// Sends a new verification link to the caller's address. The links sent before stop working, and a
/// new one can be asked for once a minute.
/// </summary>
internal sealed class ResendVerificationHandler(
    IUserRepository users,
    IUserTokenRepository tokens,
    EmailVerificationTokens verificationTokens,
    TimeProvider timeProvider) : ICommandHandler<ResendVerificationCommand>
{
    public async Task<Result> Handle(ResendVerificationCommand command, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        if (user.IsEmailVerified)
        {
            return UserTokenErrors.AlreadyVerified;
        }

        var now = timeProvider.GetUtcNow();
        var lastIssuedAt = await tokens.GetLastIssuedAtAsync(user.Id, UserTokenPurpose.EmailVerification, cancellationToken).ConfigureAwait(false);
        if (lastIssuedAt > now - UserToken.EmailVerificationCooldown)
        {
            return UserTokenErrors.RequestedRecently;
        }

        // Tracked, not ExecuteUpdate: if the command fails, the old links keep working.
        foreach (var previous in await tokens.GetUnusedAsync(user.Id, UserTokenPurpose.EmailVerification, cancellationToken).ConfigureAwait(false))
        {
            previous.Revoke(now);
        }

        verificationTokens.Issue(user.Id);
        return Result.Success();
    }
}
