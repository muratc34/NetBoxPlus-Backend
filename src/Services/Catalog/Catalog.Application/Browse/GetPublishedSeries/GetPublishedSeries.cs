using Catalog.Application.Abstractions;
using Catalog.Application.Content;
using Catalog.Domain.Content;
using Catalog.Domain.Shows;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Browse.GetPublishedSeries;

/// <summary>A published series with its published episodes; a season with none does not show.</summary>
public sealed record GetPublishedSeriesQuery(Guid SeriesId) : IQuery<PublishedSeries>;

public sealed record PublishedSeries(
    Guid Id,
    string Title,
    string? OriginalTitle,
    string Synopsis,
    int ReleaseYear,
    AgeRating AgeRating,
    Uri? PosterUrl,
    Uri? BackdropUrl,
    IReadOnlyList<GenreReference> Genres,
    IReadOnlyList<CreditResponse> Credits,
    IReadOnlyList<PublishedSeason> Seasons);

public sealed record PublishedSeason(Guid Id, int Number, string? Title, IReadOnlyList<PublishedEpisode> Episodes);

public sealed record PublishedEpisode(Guid Id, int Number, string Title, string Synopsis, int RuntimeMinutes);

internal sealed class GetPublishedSeriesHandler(ICatalogReadModel readModel, ContentReader reader) : IQueryHandler<GetPublishedSeriesQuery, PublishedSeries>
{
    public async Task<Result<PublishedSeries>> Handle(GetPublishedSeriesQuery query, CancellationToken cancellationToken)
    {
        var series = await readModel.WithSeasons(readModel.Series)
            .FirstOrDefaultAsync(series => series.Id == query.SeriesId && series.Status == PublicationStatus.Published, cancellationToken)
            .ConfigureAwait(false);

        if (series is null)
        {
            return SeriesErrors.NotFound;
        }

        var names = await reader.LoadNamesAsync([series], cancellationToken).ConfigureAwait(false);
        var seasons = series.Seasons
            .OrderBy(season => season.Number)
            .Select(season => new PublishedSeason(
                season.Id,
                season.Number,
                season.Title,
                [.. season.Episodes
                    .Where(episode => episode.IsPublished)
                    .OrderBy(episode => episode.Number)
                    .Select(episode => new PublishedEpisode(episode.Id, episode.Number, episode.Title, episode.Synopsis, episode.RuntimeMinutes))]))
            .Where(season => season.Episodes.Count > 0)
            .ToList();

        return new PublishedSeries(
            series.Id,
            series.Info.Title,
            series.Info.OriginalTitle,
            series.Info.Synopsis,
            series.Info.ReleaseYear,
            series.Info.AgeRating,
            names.ImageAddress(series.PosterImage),
            names.ImageAddress(series.BackdropImage),
            names.GenresOf(series),
            names.CreditsOf(series),
            seasons);
    }
}
