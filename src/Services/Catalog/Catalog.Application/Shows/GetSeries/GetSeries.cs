using Catalog.Application.Abstractions;
using Catalog.Application.Content;
using Catalog.Domain.Content;
using Catalog.Domain.Shows;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Shows.GetSeries;

public sealed record GetSeriesQuery(Guid SeriesId) : IQuery<SeriesDetails>;

/// <param name="Version">To send back with a change to the series, its seasons or its episodes.</param>
public sealed record SeriesDetails(
    Guid Id,
    string Title,
    string? OriginalTitle,
    string Synopsis,
    int ReleaseYear,
    AgeRating AgeRating,
    PublicationStatus Status,
    DateTimeOffset? PublishedAt,
    int Version,
    Uri? PosterUrl,
    Uri? BackdropUrl,
    IReadOnlyList<GenreReference> Genres,
    IReadOnlyList<CreditResponse> Credits,
    IReadOnlyList<SeasonDetails> Seasons,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt);

public sealed record SeasonDetails(Guid Id, int Number, string? Title, IReadOnlyList<EpisodeResponse> Episodes);

public sealed record EpisodeResponse(Guid Id, int Number, string Title, string Synopsis, int RuntimeMinutes, PublicationStatus Status, DateTimeOffset? PublishedAt);

internal sealed class GetSeriesHandler(ICatalogReadModel readModel, ContentReader reader) : IQueryHandler<GetSeriesQuery, SeriesDetails>
{
    public async Task<Result<SeriesDetails>> Handle(GetSeriesQuery query, CancellationToken cancellationToken)
    {
        var series = await readModel.WithSeasons(readModel.Series)
            .FirstOrDefaultAsync(series => series.Id == query.SeriesId, cancellationToken)
            .ConfigureAwait(false);

        if (series is null)
        {
            return SeriesErrors.NotFound;
        }

        var names = await reader.LoadNamesAsync([series], cancellationToken).ConfigureAwait(false);
        return new SeriesDetails(
            series.Id,
            series.Info.Title,
            series.Info.OriginalTitle,
            series.Info.Synopsis,
            series.Info.ReleaseYear,
            series.Info.AgeRating,
            series.Status,
            series.PublishedAt,
            series.Version,
            names.ImageAddress(series.PosterImage),
            names.ImageAddress(series.BackdropImage),
            names.GenresOf(series),
            names.CreditsOf(series),
            [.. series.Seasons.OrderBy(season => season.Number).Select(season => new SeasonDetails(
                season.Id,
                season.Number,
                season.Title,
                [.. season.Episodes.OrderBy(episode => episode.Number).Select(episode => new EpisodeResponse(
                    episode.Id,
                    episode.Number,
                    episode.Title,
                    episode.Synopsis,
                    episode.RuntimeMinutes,
                    episode.Status,
                    episode.PublishedAt))]))],
            series.CreatedAt,
            series.ModifiedAt);
    }
}
