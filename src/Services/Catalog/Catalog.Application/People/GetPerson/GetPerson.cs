using Catalog.Application.Abstractions;
using Catalog.Application.People.GetPeople;
using Catalog.Domain.People;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.People.GetPerson;

public sealed record GetPersonQuery(Guid PersonId) : IQuery<PersonResponse>;

internal sealed class GetPersonHandler(ICatalogReadModel readModel) : IQueryHandler<GetPersonQuery, PersonResponse>
{
    public async Task<Result<PersonResponse>> Handle(GetPersonQuery query, CancellationToken cancellationToken)
    {
        var person = await readModel.People
            .Where(person => person.Id == query.PersonId)
            .Select(person => new PersonResponse(person.Id, person.Name, person.Biography))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return person is null ? PersonErrors.NotFound : person;
    }
}
