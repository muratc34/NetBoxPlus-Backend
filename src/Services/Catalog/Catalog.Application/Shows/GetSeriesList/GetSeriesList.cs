using Catalog.Application.Abstractions;
using Catalog.Application.Content;
using Catalog.Domain.Content;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Application.Paging;
using Shared.Application.Validation;
using Shared.Domain;

namespace Catalog.Application.Shows.GetSeriesList;

/// <summary>Every series for the admin panel, drafts included, newest first.</summary>
public sealed record GetSeriesListQuery(string? Search, PublicationStatus? Status, Guid? GenreId, int PageNumber, int PageSize) : IQuery<Page<SeriesSummary>>;

public sealed record SeriesSummary(
    Guid Id,
    string Title,
    int ReleaseYear,
    AgeRating AgeRating,
    PublicationStatus Status,
    DateTimeOffset? PublishedAt,
    int SeasonCount,
    int EpisodeCount,
    Uri? PosterUrl,
    IReadOnlyList<GenreReference> Genres);

internal sealed class GetSeriesListValidator : IValidator<GetSeriesListQuery>
{
    public IEnumerable<ValidationFailure> Validate(GetSeriesListQuery instance)
    {
        if (!PageLimits.IsValid(instance.PageNumber, instance.PageSize))
        {
            yield return new ValidationFailure("Page", PageLimits.OutOfRange.Code, PageLimits.OutOfRange.Message);
        }
    }
}

internal sealed class GetSeriesListHandler(ICatalogReadModel readModel, ContentReader reader) : IQueryHandler<GetSeriesListQuery, Page<SeriesSummary>>
{
    public async Task<Result<Page<SeriesSummary>>> Handle(GetSeriesListQuery query, CancellationToken cancellationToken)
    {
        var list = string.IsNullOrWhiteSpace(query.Search) ? readModel.Series : readModel.SeriesTitled(query.Search);

        if (query.Status is { } status)
        {
            list = list.Where(series => series.Status == status);
        }

        if (query.GenreId is { } genreId)
        {
            list = list.Where(series => series.Genres.Any(genre => genre.GenreId == genreId));
        }

        var totalCount = await list.CountAsync(cancellationToken).ConfigureAwait(false);
        var page = await readModel.WithSeasons(list)
            .OrderByDescending(series => series.CreatedAt)
            .ThenByDescending(series => series.Id)
            .Skip(PageLimits.Offset(query.PageNumber, query.PageSize))
            .Take(query.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var names = await reader.LoadNamesAsync(page, cancellationToken).ConfigureAwait(false);
        var items = page
            .Select(series => new SeriesSummary(
                series.Id,
                series.Info.Title,
                series.Info.ReleaseYear,
                series.Info.AgeRating,
                series.Status,
                series.PublishedAt,
                series.Seasons.Count,
                series.Seasons.Sum(season => season.Episodes.Count),
                names.ImageAddress(series.PosterImage),
                names.GenresOf(series)))
            .ToList();

        return new Page<SeriesSummary>(items, query.PageNumber, query.PageSize, totalCount);
    }
}
