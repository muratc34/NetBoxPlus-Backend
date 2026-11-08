using Catalog.Application.Abstractions;
using Catalog.Domain.People;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.People.DeletePerson;

/// <summary>Deletes someone no movie or series credits, deleted content included.</summary>
public sealed record DeletePersonCommand(Guid PersonId) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => PersonId;
}

internal sealed class DeletePersonHandler(IPersonRepository people, ICatalogReadModel readModel) : ICommandHandler<DeletePersonCommand>
{
    public async Task<Result> Handle(DeletePersonCommand command, CancellationToken cancellationToken)
    {
        var person = await people.GetByIdAsync(command.PersonId, cancellationToken).ConfigureAwait(false);
        if (person is null)
        {
            return PersonErrors.NotFound;
        }

        var inUse = await readModel.MoviesIncludingDeleted.AnyAsync(movie => movie.Credits.Any(credit => credit.PersonId == command.PersonId), cancellationToken).ConfigureAwait(false)
            || await readModel.SeriesIncludingDeleted.AnyAsync(series => series.Credits.Any(credit => credit.PersonId == command.PersonId), cancellationToken).ConfigureAwait(false);
        if (inUse)
        {
            return PersonErrors.InUse;
        }

        people.Remove(person);
        return Result.Success();
    }
}
