using Catalog.Application.Abstractions;
using Catalog.Application.Content;
using Catalog.Domain.Content;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Catalog.Application.Browse.GetPublishedGenres;

/// <summary>The genres that have something published in them.</summary>
public sealed record GetPublishedGenresQuery(string Language) : IQuery<IReadOnlyList<GenreReference>>;

internal sealed class GetPublishedGenresValidator : IValidator<GetPublishedGenresQuery>
{
    public IEnumerable<ValidationFailure> Validate(GetPublishedGenresQuery instance) => LanguageValidation.Validate(instance.Language);
}

internal sealed class GetPublishedGenresHandler(ICatalogReadModel readModel) : IQueryHandler<GetPublishedGenresQuery, IReadOnlyList<GenreReference>>
{
    public async Task<Result<IReadOnlyList<GenreReference>>> Handle(GetPublishedGenresQuery query, CancellationToken cancellationToken)
    {
        var genres = await readModel.Genres
            .Where(genre => readModel.Movies.Any(movie => movie.Status == PublicationStatus.Published && movie.Genres.Any(item => item.GenreId == genre.Id))
                || readModel.Series.Any(series => series.Status == PublicationStatus.Published && series.Genres.Any(item => item.GenreId == genre.Id)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return genres
            .Select(genre => new GenreReference(genre.Id, genre.NameFor(query.Language)))
            .OrderBy(genre => genre.Name, StringComparer.Ordinal)
            .ToList();
    }
}
