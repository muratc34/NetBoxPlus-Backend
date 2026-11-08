using Catalog.Application.Abstractions;
using Catalog.Domain.Content;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Application.Paging;
using Shared.Application.Validation;
using Shared.Domain;

namespace Catalog.Application.People.GetPeople;

/// <summary>People by name.</summary>
public sealed record GetPeopleQuery(string? Search, int PageNumber, int PageSize) : IQuery<Page<PersonResponse>>;

public sealed record PersonResponse(Guid Id, string Name, string? Biography, IReadOnlyDictionary<string, string> Translations, Uri? PhotoUrl);

internal sealed class GetPeopleValidator : IValidator<GetPeopleQuery>
{
    public IEnumerable<ValidationFailure> Validate(GetPeopleQuery instance)
    {
        if (!PageLimits.IsValid(instance.PageNumber, instance.PageSize))
        {
            yield return new ValidationFailure("Page", PageLimits.OutOfRange.Code, PageLimits.OutOfRange.Message);
        }
    }
}

internal sealed class GetPeopleHandler(ICatalogReadModel readModel, IImageStorage storage) : IQueryHandler<GetPeopleQuery, Page<PersonResponse>>
{
    public async Task<Result<Page<PersonResponse>>> Handle(GetPeopleQuery query, CancellationToken cancellationToken)
    {
        var people = string.IsNullOrWhiteSpace(query.Search) ? readModel.People : readModel.PeopleNamed(query.Search);

        var totalCount = await people.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await people
            .OrderBy(person => person.Name)
            .ThenBy(person => person.Id)
            .Skip(PageLimits.Offset(query.PageNumber, query.PageSize))
            .Take(query.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var responses = items
            .Select(person => new PersonResponse(person.Id, person.Name, person.BiographyFor(Languages.Default)?.Biography, person.Translations.ToDictionary(item => item.Language, item => item.Biography), person.PhotoImage is { } photo ? storage.PublicAddress(photo) : null))
            .ToList();

        return new Page<PersonResponse>(responses, query.PageNumber, query.PageSize, totalCount);
    }
}
