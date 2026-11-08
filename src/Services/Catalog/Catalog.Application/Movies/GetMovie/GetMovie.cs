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
    string Synopsis,
    int ReleaseYear,
    AgeRating AgeRating,
    int RuntimeMinutes,
    PublicationStatus Status,
    DateTimeOffset? PublishedAt,
    int Version,
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
            movie.Info.Title,
            movie.Info.OriginalTitle,
            movie.Info.Synopsis,
            movie.Info.ReleaseYear,
            movie.Info.AgeRating,
            movie.RuntimeMinutes,
            movie.Status,
            movie.PublishedAt,
            movie.Version,
            names.GenresOf(movie),
            names.CreditsOf(movie),
            movie.CreatedAt,
            movie.ModifiedAt);
    }
}
