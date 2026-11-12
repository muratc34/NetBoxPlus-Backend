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
    IReadOnlyDictionary<string, ContentTextFields> Translations,
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

public sealed record SeasonDetails(Guid Id, int Number, string? Title, IReadOnlyDictionary<string, string> Translations, IReadOnlyList<EpisodeResponse> Episodes);

public sealed record EpisodeResponse(Guid Id, int Number, string Title, IReadOnlyDictionary<string, ContentTextFields> Translations, int RuntimeMinutes, PublicationStatus Status, DateTimeOffset? PublishedAt, VideoState VideoStatus, bool HasPlayableVideo);

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
            series.TextFor(Languages.Default).Title,
            series.Info.OriginalTitle,
            series.Translations.ToDictionary(item => item.Language, item => new ContentTextFields(item.Title, item.Synopsis)),
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
                season.TitleFor(Languages.Default),
                season.Translations.ToDictionary(item => item.Language, item => item.Title),
                [.. season.Episodes.OrderBy(episode => episode.Number).Select(episode => new EpisodeResponse(
                    episode.Id,
                    episode.Number,
                    episode.TextFor(Languages.Default).Title,
                    episode.Translations.ToDictionary(item => item.Language, item => new ContentTextFields(item.Title, item.Synopsis)),
                    episode.RuntimeMinutes,
                    episode.Status,
                    episode.PublishedAt,
                    episode.Video.LatestState,
                    episode.Video.IsPlayable))]))],
            series.CreatedAt,
            series.ModifiedAt);
    }
}
