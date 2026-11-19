using Auth.Domain.Roles;
using Shared.Application;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Auth.Application.Roles.DeleteRole;

/// <summary>Deletes the role; its holders lose its permissions in their next access token.</summary>
public sealed record DeleteRoleCommand(Guid RoleId) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => RoleId;
}

internal sealed class DeleteRoleHandler(IRoleRepository roles, ICurrentUser currentUser) : ICommandHandler<DeleteRoleCommand>
{
    public async Task<Result> Handle(DeleteRoleCommand command, CancellationToken cancellationToken)
    {
        var role = await roles.GetByIdAsync(command.RoleId, cancellationToken).ConfigureAwait(false);
        if (role is null)
        {
            return RoleErrors.NotFound;
        }

        if (role.IsSystem)
        {
            return RoleErrors.SystemRole;
        }

        var held = currentUser.EnsureHolds(role.Permissions);
        if (held.IsFailure)
        {
            return held;
        }

        roles.Remove(role);
        return Result.Success();
    }
}
