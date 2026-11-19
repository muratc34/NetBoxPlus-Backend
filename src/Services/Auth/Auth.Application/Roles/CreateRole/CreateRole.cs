using Auth.Domain.Roles;
using Shared.Application;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Auth.Application.Roles.CreateRole;

public sealed record CreateRoleCommand(string Name, IReadOnlyList<string> Permissions) : ICommand<Guid>, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => null;
}

internal sealed class CreateRoleValidator : IValidator<CreateRoleCommand>
{
    public IEnumerable<ValidationFailure> Validate(CreateRoleCommand instance)
        => RoleValidation.Validate(instance.Name, instance.Permissions);
}

internal sealed class CreateRoleHandler(IRoleRepository roles, ICurrentUser currentUser) : ICommandHandler<CreateRoleCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateRoleCommand command, CancellationToken cancellationToken)
    {
        var held = currentUser.EnsureHolds(command.Permissions);
        if (held.IsFailure)
        {
            return held.Error;
        }

        var role = Role.Create(command.Name, command.Permissions);
        if (role.IsFailure)
        {
            return role.Error;
        }

        if (await roles.ExistsByNameAsync(role.Value.Name, cancellationToken).ConfigureAwait(false))
        {
            return RoleErrors.NameTaken;
        }

        roles.Add(role.Value);
        return role.Value.Id;
    }
}
