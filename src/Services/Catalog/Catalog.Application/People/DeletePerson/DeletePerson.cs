using Catalog.Application.Abstractions;
using Catalog.Domain.Movies;
using Catalog.Domain.People;
using Catalog.Domain.Shows;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.People.DeletePerson;

/// <summary>
/// Deletes someone no movie or series credits. Deleted content does not count: it lets go of the person in
/// the same transaction (its credits would hold the person's row).
/// </summary>
public sealed record DeletePersonCommand(Guid PersonId) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => PersonId;
}

internal sealed class DeletePersonHandler(IPersonRepository people, IMovieRepository movies, ISeriesRepository series, ICatalogReadModel readModel)
    : ICommandHandler<DeletePersonCommand>
{
    public async Task<Result> Handle(DeletePersonCommand command, CancellationToken cancellationToken)
    {
        var person = await people.GetByIdAsync(command.PersonId, cancellationToken).ConfigureAwait(false);
        if (person is null)
        {
            return PersonErrors.NotFound;
        }

        var inUse = await readModel.Movies.AnyAsync(movie => movie.Credits.Any(credit => credit.PersonId == command.PersonId), cancellationToken).ConfigureAwait(false)
            || await readModel.Series.AnyAsync(show => show.Credits.Any(credit => credit.PersonId == command.PersonId), cancellationToken).ConfigureAwait(false);
        if (inUse)
        {
            return PersonErrors.InUse;
        }

        foreach (var movie in await movies.ListDeletedUsingAsync(genreId: null, command.PersonId, cancellationToken).ConfigureAwait(false))
        {
            movie.ReleasePerson(command.PersonId);
        }

        foreach (var show in await series.ListDeletedUsingAsync(genreId: null, command.PersonId, cancellationToken).ConfigureAwait(false))
        {
            show.ReleasePerson(command.PersonId);
        }

        people.Remove(person);
        return Result.Success();
    }
}
