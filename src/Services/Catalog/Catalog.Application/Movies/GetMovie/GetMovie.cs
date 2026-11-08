using Catalog.Application.Abstractions;
using Catalog.Application.Content;
using Catalog.Domain.Content;
using Catalog.Domain.Movies;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Movies.GetMovie;

public sealed record GetMovieQuery(Guid MovieId) : IQuery<MovieDetails>;

/// <param name="Version">To send back with a change.</param>
public sealed record MovieDetails(
    Guid Id,
    string Title,
    string? OriginalTitle,
    IReadOnlyDictionary<string, ContentTextFields> Translations,
    int ReleaseYear,
    AgeRating AgeRating,
    int RuntimeMinutes,
    PublicationStatus Status,
    DateTimeOffset? PublishedAt,
    int Version,
    Uri? PosterUrl,
    Uri? BackdropUrl,
    IReadOnlyList<GenreReference> Genres,
    IReadOnlyList<CreditResponse> Credits,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt);

internal sealed class GetMovieHandler(ICatalogReadModel readModel, ContentReader reader) : IQueryHandler<GetMovieQuery, MovieDetails>
{
    public async Task<Result<MovieDetails>> Handle(GetMovieQuery query, CancellationToken cancellationToken)
    {
        var movie = await readModel.Movies.FirstOrDefaultAsync(movie => movie.Id == query.MovieId, cancellationToken).ConfigureAwait(false);
        if (movie is null)
        {
            return MovieErrors.NotFound;
        }

        var names = await reader.LoadNamesAsync([movie], cancellationToken).ConfigureAwait(false);
        return new MovieDetails(
            movie.Id,
            movie.TextFor(Languages.Default).Title,
            movie.Info.OriginalTitle,
            movie.Translations.ToDictionary(item => item.Language, item => new ContentTextFields(item.Title, item.Synopsis)),
            movie.Info.ReleaseYear,
            movie.Info.AgeRating,
            movie.RuntimeMinutes,
            movie.Status,
            movie.PublishedAt,
            movie.Version,
            names.ImageAddress(movie.PosterImage),
            names.ImageAddress(movie.BackdropImage),
            names.GenresOf(movie),
            names.CreditsOf(movie),
            movie.CreatedAt,
            movie.ModifiedAt);
    }
}
