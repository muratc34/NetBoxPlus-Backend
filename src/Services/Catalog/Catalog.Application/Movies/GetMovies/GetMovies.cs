using Catalog.Application.Abstractions;
using Catalog.Application.Content;
using Catalog.Domain.Content;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Application.Paging;
using Shared.Application.Validation;
using Shared.Domain;

namespace Catalog.Application.Movies.GetMovies;

/// <summary>Every movie for the admin panel, drafts included, newest first.</summary>
public sealed record GetMoviesQuery(string? Search, PublicationStatus? Status, Guid? GenreId, int PageNumber, int PageSize) : IQuery<Page<MovieSummary>>;

public sealed record MovieSummary(
    Guid Id,
    string Title,
    int ReleaseYear,
    AgeRating AgeRating,
    int RuntimeMinutes,
    PublicationStatus Status,
    DateTimeOffset? PublishedAt,
    Uri? PosterUrl,
    IReadOnlyList<GenreReference> Genres);

internal sealed class GetMoviesValidator : IValidator<GetMoviesQuery>
{
    public IEnumerable<ValidationFailure> Validate(GetMoviesQuery instance)
    {
        if (!PageLimits.IsValid(instance.PageNumber, instance.PageSize))
        {
            yield return new ValidationFailure("Page", PageLimits.OutOfRange.Code, PageLimits.OutOfRange.Message);
        }
    }
}

internal sealed class GetMoviesHandler(ICatalogReadModel readModel, ContentReader reader) : IQueryHandler<GetMoviesQuery, Page<MovieSummary>>
{
    public async Task<Result<Page<MovieSummary>>> Handle(GetMoviesQuery query, CancellationToken cancellationToken)
    {
        var movies = string.IsNullOrWhiteSpace(query.Search) ? readModel.Movies : readModel.MoviesTitled(query.Search);

        if (query.Status is { } status)
        {
            movies = movies.Where(movie => movie.Status == status);
        }

        if (query.GenreId is { } genreId)
        {
            movies = movies.Where(movie => movie.Genres.Any(genre => genre.GenreId == genreId));
        }

        var totalCount = await movies.CountAsync(cancellationToken).ConfigureAwait(false);
        var page = await movies
            .OrderByDescending(movie => movie.CreatedAt)
            .ThenByDescending(movie => movie.Id)
            .Skip(PageLimits.Offset(query.PageNumber, query.PageSize))
            .Take(query.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var names = await reader.LoadNamesAsync(page, cancellationToken).ConfigureAwait(false);
        var items = page
            .Select(movie => new MovieSummary(
                movie.Id,
                movie.Info.Title,
                movie.Info.ReleaseYear,
                movie.Info.AgeRating,
                movie.RuntimeMinutes,
                movie.Status,
                movie.PublishedAt,
                names.ImageAddress(movie.PosterImage),
                names.GenresOf(movie)))
            .ToList();

        return new Page<MovieSummary>(items, query.PageNumber, query.PageSize, totalCount);
    }
}
