using Auth.Application.Roles;
using Auth.Application.Users.DeleteAccount;
using Auth.Domain.Roles;
using Auth.Domain.Users;
using Shared.Application;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Auth.Application.Users.DeleteUser;

/// <summary>An administrator deletes an account. There is no undo.</summary>
public sealed record DeleteUserCommand(Guid UserId) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => UserId;
}

/// <summary>
/// Not the caller's own account (the account's own deletion asks for its password). An account with
/// roles can be deleted only by a caller who holds every permission of those roles, as taking the
/// roles away would require; and the last holder of the Administrator role stays.
/// </summary>
internal sealed class DeleteUserHandler(
    IUserRepository users,
    IRoleRepository roles,
    ICurrentUser currentUser,
    AccountDeletion deletion) : ICommandHandler<DeleteUserCommand>
{
    public async Task<Result> Handle(DeleteUserCommand command, CancellationToken cancellationToken)
    {
        if (command.UserId == currentUser.Id)
        {
            return UserErrors.CannotDeleteSelf;
        }

        var user = await users.GetByIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        foreach (var userRole in user.Roles)
        {
            var role = await roles.GetByIdAsync(userRole.RoleId, cancellationToken).ConfigureAwait(false);
            if (role is null)
            {
                continue;
            }

            var held = currentUser.EnsureHolds(role.Permissions);
            if (held.IsFailure)
            {
                return held;
            }

            if (role.IsSystem && await users.CountWithRoleAsync(role.Id, cancellationToken).ConfigureAwait(false) <= 1)
            {
                return UserErrors.LastAdministrator;
            }
        }

        await deletion.DeleteAsync(user, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
