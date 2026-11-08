using Catalog.Application.Abstractions;
using Catalog.Application.People.GetPeople;
using Catalog.Domain.People;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.People.GetPerson;

public sealed record GetPersonQuery(Guid PersonId) : IQuery<PersonResponse>;

internal sealed class GetPersonHandler(ICatalogReadModel readModel, IImageStorage storage) : IQueryHandler<GetPersonQuery, PersonResponse>
{
    public async Task<Result<PersonResponse>> Handle(GetPersonQuery query, CancellationToken cancellationToken)
    {
        var person = await readModel.People
            .FirstOrDefaultAsync(person => person.Id == query.PersonId, cancellationToken)
            .ConfigureAwait(false);

        return person is null
            ? PersonErrors.NotFound
            : new PersonResponse(person.Id, person.Name, person.Biography, person.PhotoImage is { } photo ? storage.PublicAddress(photo) : null);
    }
}
