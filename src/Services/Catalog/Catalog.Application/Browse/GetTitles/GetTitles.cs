using Catalog.Application.Abstractions;
using Catalog.Application.Content;
using Catalog.Domain.Content;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Application.Paging;
using Shared.Application.Validation;
using Shared.Domain;

namespace Catalog.Application.Browse.GetTitles;

public enum TitleKind
{
    Movie,
    Series,
}

/// <summary>Published movies and series in one list, most recently published first.</summary>
public sealed record GetTitlesQuery(TitleKind? Kind, Guid? GenreId, Guid? PersonId, int PageNumber, int PageSize) : IQuery<Page<TitleSummary>>;

public sealed record TitleSummary(
    Guid Id,
    TitleKind Kind,
    string Title,
    int ReleaseYear,
    AgeRating AgeRating,
    Uri? PosterUrl,
    IReadOnlyList<GenreReference> Genres);

internal sealed class GetTitlesValidator : IValidator<GetTitlesQuery>
{
    public IEnumerable<ValidationFailure> Validate(GetTitlesQuery instance)
    {
        if (!PageLimits.IsValid(instance.PageNumber, instance.PageSize))
        {
            yield return new ValidationFailure("Page", PageLimits.OutOfRange.Code, PageLimits.OutOfRange.Message);
        }
    }
}

internal sealed class GetTitlesHandler(ICatalogReadModel readModel, ContentReader reader) : IQueryHandler<GetTitlesQuery, Page<TitleSummary>>
{
    public async Task<Result<Page<TitleSummary>>> Handle(GetTitlesQuery query, CancellationToken cancellationToken)
    {
        var movies = readModel.Movies
            .Where(movie => movie.Status == PublicationStatus.Published
                && (query.GenreId == null || movie.Genres.Any(genre => genre.GenreId == query.GenreId))
                && (query.PersonId == null || movie.Credits.Any(credit => credit.PersonId == query.PersonId)))
            .Select(movie => new { movie.Id, Kind = TitleKind.Movie, movie.PublishedAt });

        var series = readModel.Series
            .Where(item => item.Status == PublicationStatus.Published
                && (query.GenreId == null || item.Genres.Any(genre => genre.GenreId == query.GenreId))
                && (query.PersonId == null || item.Credits.Any(credit => credit.PersonId == query.PersonId)))
            .Select(item => new { item.Id, Kind = TitleKind.Series, item.PublishedAt });

        var titles = query.Kind switch
        {
            TitleKind.Movie => movies,
            TitleKind.Series => series,
            _ => movies.Concat(series),
        };

        var totalCount = await titles.CountAsync(cancellationToken).ConfigureAwait(false);
        var page = await titles
            .OrderByDescending(title => title.PublishedAt)
            .ThenByDescending(title => title.Id)
            .Skip(PageLimits.Offset(query.PageNumber, query.PageSize))
            .Take(query.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var ids = page.Select(title => title.Id).ToList();
        var loadedMovies = await readModel.Movies.Where(movie => ids.Contains(movie.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var loadedSeries = await readModel.Series.Where(item => ids.Contains(item.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);

        var content = loadedMovies.Cast<ContentItem>().Concat(loadedSeries).ToDictionary(item => item.Id);
        var names = await reader.LoadNamesAsync(content.Values, cancellationToken).ConfigureAwait(false);

        // A title deleted between the two reads is simply left out of this page.
        var items = page
            .Where(title => content.ContainsKey(title.Id))
            .Select(title => (title.Kind, Item: content[title.Id]))
            .Select(title => new TitleSummary(title.Item.Id, title.Kind, title.Item.Info.Title, title.Item.Info.ReleaseYear, title.Item.Info.AgeRating, names.ImageAddress(title.Item.PosterImage), names.GenresOf(title.Item)))
            .ToList();

        return new Page<TitleSummary>(items, query.PageNumber, query.PageSize, totalCount);
    }
}
