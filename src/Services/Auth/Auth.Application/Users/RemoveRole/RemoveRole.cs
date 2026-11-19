using Auth.Application.Roles;
using Auth.Domain.Roles;
using Auth.Domain.Users;
using Shared.Application;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Auth.Application.Users.RemoveRole;

/// <summary>Takes a role away; the user keeps its permissions until their next access token.</summary>
public sealed record RemoveRoleCommand(Guid UserId, Guid RoleId) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => UserId;
}

internal sealed class RemoveRoleHandler(IUserRepository users, IRoleRepository roles, ICurrentUser currentUser)
    : ICommandHandler<RemoveRoleCommand>
{
    public async Task<Result> Handle(RemoveRoleCommand command, CancellationToken cancellationToken)
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

        var role = await roles.GetByIdAsync(command.RoleId, cancellationToken).ConfigureAwait(false);
        if (role is null)
        {
            return RoleErrors.NotFound;
        }

        var held = currentUser.EnsureHolds(role.Permissions);
        if (held.IsFailure)
        {
            return held;
        }

        user.RemoveRole(role.Id);
        return Result.Success();
    }
}
