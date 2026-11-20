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

public enum TitleSort
{
    /// <summary>Most recently published first.</summary>
    Newest,

    /// <summary>Watched most in the last week first; titles nobody watched follow, newest first.</summary>
    Popular,
}

public static class TitleListErrors
{
    /// <summary>As many as one page holds: a client asking by id gets all of them on one page.</summary>
    public const int MaxIds = PageLimits.MaxPageSize;

    public static readonly Error TooManyIds = Error.Validation("Titles.TooManyIds", $"Ask for at most {MaxIds} titles by id at once.");
}

/// <summary>Published movies and series in one list, most recently published first unless sorted otherwise.</summary>
/// <param name="Language">Texts come in this language, or in the default one where not translated.</param>
/// <param name="MaxAgeRating">Only titles a viewer of this age may see, such as a child's profile; all when null.</param>
/// <param name="Ids">
/// Only these titles, such as the ones another service names by id (a profile's list); all when null or empty.
/// Unknown and unpublished ids are left out, and the list keeps its own order, not the order of the ids.
/// </param>
public sealed record GetTitlesQuery(string Language, TitleKind? Kind, Guid? GenreId, Guid? PersonId, int PageNumber, int PageSize, TitleSort Sort = TitleSort.Newest, AgeRating? MaxAgeRating = null, IReadOnlyList<Guid>? Ids = null) : IQuery<Page<TitleSummary>>;

/// <param name="PosterUrl">The portrait image (2:3), when the title has one.</param>
/// <param name="BackdropUrl">The landscape image (16:9), when the title has one; what rows of wide cards show.</param>
public sealed record TitleSummary(
    Guid Id,
    TitleKind Kind,
    string Title,
    string Language,
    int ReleaseYear,
    AgeRating AgeRating,
    Uri? PosterUrl,
    Uri? BackdropUrl,
    IReadOnlyList<GenreReference> Genres);

internal sealed class GetTitlesValidator : IValidator<GetTitlesQuery>
{
    public IEnumerable<ValidationFailure> Validate(GetTitlesQuery instance)
    {
        foreach (var failure in LanguageValidation.Validate(instance.Language))
        {
            yield return failure;
        }

        if (!PageLimits.IsValid(instance.PageNumber, instance.PageSize))
        {
            yield return new ValidationFailure("Page", PageLimits.OutOfRange.Code, PageLimits.OutOfRange.Message);
        }

        if (instance.Ids is { Count: > TitleListErrors.MaxIds })
        {
            yield return new ValidationFailure("ids", TitleListErrors.TooManyIds.Code, TitleListErrors.TooManyIds.Message);
        }
    }
}

internal sealed class GetTitlesHandler(ICatalogReadModel readModel, ContentReader reader) : IQueryHandler<GetTitlesQuery, Page<TitleSummary>>
{
    public async Task<Result<Page<TitleSummary>>> Handle(GetTitlesQuery query, CancellationToken cancellationToken)
    {
        var publishedMovies = readModel.Movies
            .Where(movie => movie.Status == PublicationStatus.Published
                && (query.GenreId == null || movie.Genres.Any(genre => genre.GenreId == query.GenreId))
                && (query.PersonId == null || movie.Credits.Any(credit => credit.PersonId == query.PersonId)));

        var publishedSeries = readModel.Series
            .Where(item => item.Status == PublicationStatus.Published
                && (query.GenreId == null || item.Genres.Any(genre => genre.GenreId == query.GenreId))
                && (query.PersonId == null || item.Credits.Any(credit => credit.PersonId == query.PersonId)));

        // Filtered here, not by the client: dropping titles from a page would leave it short and the count wrong.
        if (query.MaxAgeRating is { } maximum)
        {
            var allowed = AgeRatings.UpTo(maximum);
            publishedMovies = publishedMovies.Where(movie => allowed.Contains(movie.Info.AgeRating));
            publishedSeries = publishedSeries.Where(item => allowed.Contains(item.Info.AgeRating));
        }

        if (query.Ids is { Count: > 0 } wanted)
        {
            var wantedIds = wanted.Distinct().ToList();
            publishedMovies = publishedMovies.Where(movie => wantedIds.Contains(movie.Id));
            publishedSeries = publishedSeries.Where(item => wantedIds.Contains(item.Id));
        }

        var movies = publishedMovies.Select(movie => new { movie.Id, Kind = TitleKind.Movie, movie.PublishedAt });
        var series = publishedSeries.Select(item => new { item.Id, Kind = TitleKind.Series, item.PublishedAt });

        var titles = query.Kind switch
        {
            TitleKind.Movie => movies,
            TitleKind.Series => series,
            _ => movies.Concat(series),
        };

        var totalCount = await titles.CountAsync(cancellationToken).ConfigureAwait(false);
        var scored = titles.Select(title => new
        {
            title.Id,
            title.Kind,
            title.PublishedAt,
            Score = query.Sort == TitleSort.Popular
                ? readModel.TitlePopularity.Where(popularity => popularity.TitleId == title.Id).Select(popularity => popularity.Score).FirstOrDefault()
                : 0,
        });
        var page = await scored
            .OrderByDescending(title => title.Score)
            .ThenByDescending(title => title.PublishedAt)
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
            .Select(title => (title.Kind, title.Item, Text: title.Item.TextFor(query.Language)))
            .Select(title => new TitleSummary(title.Item.Id, title.Kind, title.Text.Title, title.Text.Language, title.Item.Info.ReleaseYear, title.Item.Info.AgeRating, names.ImageAddress(title.Item.PosterImage), names.ImageAddress(title.Item.BackdropImage), names.GenresOf(title.Item, query.Language)))
            .ToList();

        return new Page<TitleSummary>(items, query.PageNumber, query.PageSize, totalCount);
    }
}
