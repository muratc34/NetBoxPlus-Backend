using Auth.Domain.Roles;
using Shared.Application;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Auth.Application.Roles.UpdateRole;

/// <summary>Replaces the role's name and permissions; holders get the new permissions in their next access token.</summary>
public sealed record UpdateRoleCommand(Guid RoleId, string Name, IReadOnlyList<string> Permissions) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => RoleId;
}

internal sealed class UpdateRoleValidator : IValidator<UpdateRoleCommand>
{
    public IEnumerable<ValidationFailure> Validate(UpdateRoleCommand instance)
        => RoleValidation.Validate(instance.Name, instance.Permissions);
}

internal sealed class UpdateRoleHandler(IRoleRepository roles, ICurrentUser currentUser) : ICommandHandler<UpdateRoleCommand>
{
    public async Task<Result> Handle(UpdateRoleCommand command, CancellationToken cancellationToken)
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

        // Both what the role has and what it will have: taking a permission away is a change too.
        var held = currentUser.EnsureHolds(role.Permissions.Concat(command.Permissions));
        if (held.IsFailure)
        {
            return held.Error;
        }

        if (Role.Normalize(command.Name) != role.NormalizedName
            && await roles.ExistsByNameAsync(command.Name, cancellationToken).ConfigureAwait(false))
        {
            return RoleErrors.NameTaken;
        }

        var renamed = role.Rename(command.Name);
        return renamed.IsFailure ? renamed : role.SetPermissions(command.Permissions);
    }
}
