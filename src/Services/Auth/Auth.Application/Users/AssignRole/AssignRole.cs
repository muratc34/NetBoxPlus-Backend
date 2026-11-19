using Auth.Application.Roles;
using Auth.Domain.Roles;
using Auth.Domain.Users;
using Shared.Application;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Auth.Application.Users.AssignRole;

/// <summary>Gives the user a role; the permissions arrive with the user's next access token.</summary>
public sealed record AssignRoleCommand(Guid UserId, Guid RoleId) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => UserId;
}

internal sealed class AssignRoleHandler(IUserRepository users, IRoleRepository roles, ICurrentUser currentUser)
    : ICommandHandler<AssignRoleCommand>
{
    public async Task<Result> Handle(AssignRoleCommand command, CancellationToken cancellationToken)
    {
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

        user.AssignRole(role.Id);
        return Result.Success();
    }
}
