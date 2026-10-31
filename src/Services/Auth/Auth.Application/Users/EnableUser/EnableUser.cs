using Auth.Domain.Users;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Auth.Application.Users.EnableUser;

/// <summary>Lets the account sign in again; also lifts a lockout after failed sign-ins.</summary>
public sealed record EnableUserCommand(Guid UserId) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => UserId;
}

internal sealed class EnableUserHandler(IUserRepository users) : ICommandHandler<EnableUserCommand>
{
    public async Task<Result> Handle(EnableUserCommand command, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        user.Enable();
        return Result.Success();
    }
}
