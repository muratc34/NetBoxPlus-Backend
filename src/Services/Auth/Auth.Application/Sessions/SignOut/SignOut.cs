using Auth.Domain.RefreshTokens;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Auth.Application.Sessions.SignOut;

public sealed record SignOutCommand(string RefreshToken) : ICommand;

/// <summary>
/// Ends the session the refresh token belongs to. Always succeeds: an unknown or already revoked
/// token leaves nothing to end, and the answer tells the caller nothing about it.
/// </summary>
internal sealed class SignOutHandler(IRefreshTokenRepository refreshTokens, TimeProvider timeProvider) : ICommandHandler<SignOutCommand>
{
    public async Task<Result> Handle(SignOutCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(command.RefreshToken))
        {
            return Result.Success();
        }

        var token = await refreshTokens.GetByHashAsync(SessionTokens.Hash(command.RefreshToken), cancellationToken).ConfigureAwait(false);
        if (token is not null)
        {
            await refreshTokens.RevokeFamilyAsync(token.FamilyId, timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        }

        return Result.Success();
    }
}
