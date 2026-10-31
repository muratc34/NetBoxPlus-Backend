using Auth.Domain.RefreshTokens;
using Auth.Domain.Users;
using Shared.Application;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Auth.Application.Users.DisableUser;

/// <summary>Stops the account from signing in and ends its sessions; access tokens already issued expire on their own.</summary>
public sealed record DisableUserCommand(Guid UserId) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => UserId;
}

internal sealed class DisableUserHandler(
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<DisableUserCommand>
{
    public async Task<Result> Handle(DisableUserCommand command, CancellationToken cancellationToken)
    {
        if (command.UserId == currentUser.Id)
        {
            return UserErrors.CannotModifySelf;
        }

        var user = await users.GetByIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        user.Disable();

        // At once, like every revocation; a refresh would also refuse a disabled account.
        await refreshTokens.RevokeAllForUserAsync(user.Id, timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
