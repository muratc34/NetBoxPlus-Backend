using Catalog.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Genres.GetGenres;

/// <summary>Every genre; there are few, so the list is not paged.</summary>
public sealed record GetGenresQuery : IQuery<IReadOnlyList<GenreResponse>>;

/// <param name="UsageCount">Movies and series using it, deleted ones included: above zero it cannot be deleted.</param>
public sealed record GenreResponse(Guid Id, string Name, int UsageCount);

internal sealed class GetGenresHandler(ICatalogReadModel readModel) : IQueryHandler<GetGenresQuery, IReadOnlyList<GenreResponse>>
{
    public async Task<Result<IReadOnlyList<GenreResponse>>> Handle(GetGenresQuery query, CancellationToken cancellationToken)
    {
        var movies = await CountAsync(readModel.MoviesIncludingDeleted.SelectMany(movie => movie.Genres), cancellationToken).ConfigureAwait(false);
        var series = await CountAsync(readModel.SeriesIncludingDeleted.SelectMany(item => item.Genres), cancellationToken).ConfigureAwait(false);

        var genres = await readModel.Genres
            .OrderBy(genre => genre.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return genres
            .Select(genre => new GenreResponse(genre.Id, genre.Name, movies.GetValueOrDefault(genre.Id) + series.GetValueOrDefault(genre.Id)))
            .ToList();
    }

    private static Task<Dictionary<Guid, int>> CountAsync(IQueryable<Domain.Content.ContentGenre> uses, CancellationToken cancellationToken)
        => uses.GroupBy(use => use.GenreId)
            .Select(group => new { GenreId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(count => count.GenreId, count => count.Count, cancellationToken);
}
