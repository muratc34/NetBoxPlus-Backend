using Catalog.Domain.People;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.People.CreatePerson;

public sealed record CreatePersonCommand(string? Name, IReadOnlyDictionary<string, string?>? Biographies) : ICommand<Guid>, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => null;
}

internal sealed class CreatePersonHandler(IPersonRepository people) : ICommandHandler<CreatePersonCommand, Guid>
{
    public Task<Result<Guid>> Handle(CreatePersonCommand command, CancellationToken cancellationToken)
    {
        var person = Person.Create(command.Name, command.Biographies);
        if (person.IsFailure)
        {
            return Task.FromResult(Result.Failure<Guid>(person.Error));
        }

        people.Add(person.Value);
        return Task.FromResult(Result.Success(person.Value.Id));
    }
}
