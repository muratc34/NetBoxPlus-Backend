using Catalog.Application.Abstractions;
using Catalog.Application.Content;
using Catalog.Domain.Content;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Browse.GetPublishedGenres;

/// <summary>The genres that have something published in them.</summary>
public sealed record GetPublishedGenresQuery : IQuery<IReadOnlyList<GenreReference>>;

internal sealed class GetPublishedGenresHandler(ICatalogReadModel readModel) : IQueryHandler<GetPublishedGenresQuery, IReadOnlyList<GenreReference>>
{
    public async Task<Result<IReadOnlyList<GenreReference>>> Handle(GetPublishedGenresQuery query, CancellationToken cancellationToken)
        => await readModel.Genres
            .Where(genre => readModel.Movies.Any(movie => movie.Status == PublicationStatus.Published && movie.Genres.Any(item => item.GenreId == genre.Id))
                || readModel.Series.Any(series => series.Status == PublicationStatus.Published && series.Genres.Any(item => item.GenreId == genre.Id)))
            .OrderBy(genre => genre.Name)
            .Select(genre => new GenreReference(genre.Id, genre.Name))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
