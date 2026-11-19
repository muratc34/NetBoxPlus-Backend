using Catalog.Application.Abstractions;
using Catalog.Application.Content;
using Catalog.Domain.Content;
using Catalog.Domain.People;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Catalog.Application.Browse.GetPublicPerson;

/// <summary>Someone credited on published content; a name that only appears in drafts stays hidden.</summary>
public sealed record GetPublicPersonQuery(Guid PersonId, string Language) : IQuery<PublicPerson>;

/// <param name="Language">The language of the biography, when there is one.</param>
public sealed record PublicPerson(Guid Id, string Name, string? Biography, string? Language, Uri? PhotoUrl);

internal sealed class GetPublicPersonValidator : IValidator<GetPublicPersonQuery>
{
    public IEnumerable<ValidationFailure> Validate(GetPublicPersonQuery instance) => LanguageValidation.Validate(instance.Language);
}

internal sealed class GetPublicPersonHandler(ICatalogReadModel readModel, IImageStorage storage) : IQueryHandler<GetPublicPersonQuery, PublicPerson>
{
    public async Task<Result<PublicPerson>> Handle(GetPublicPersonQuery query, CancellationToken cancellationToken)
    {
        var person = await readModel.People
            .Where(person => person.Id == query.PersonId
                && (readModel.Movies.Any(movie => movie.Status == PublicationStatus.Published && movie.Credits.Any(credit => credit.PersonId == person.Id))
                    || readModel.Series.Any(series => series.Status == PublicationStatus.Published && series.Credits.Any(credit => credit.PersonId == person.Id))))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (person is null)
        {
            return PersonErrors.NotFound;
        }

        var biography = person.BiographyFor(query.Language);
        return new PublicPerson(person.Id, person.Name, biography?.Biography, biography?.Language, person.PhotoImage is { } photo ? storage.PublicAddress(photo) : null);
    }
}
