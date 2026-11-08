using Catalog.Domain.People;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.People.UpdatePerson;

public sealed record UpdatePersonCommand(Guid PersonId, string? Name, string? Biography) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => PersonId;
}

internal sealed class UpdatePersonHandler(IPersonRepository people) : ICommandHandler<UpdatePersonCommand>
{
    public async Task<Result> Handle(UpdatePersonCommand command, CancellationToken cancellationToken)
    {
        var person = await people.GetByIdAsync(command.PersonId, cancellationToken).ConfigureAwait(false);
        return person is null ? PersonErrors.NotFound : person.Change(command.Name, command.Biography);
    }
}
