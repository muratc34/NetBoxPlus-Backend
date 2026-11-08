using Catalog.Application.Abstractions;
using Catalog.Application.Content;
using Catalog.Domain.Content;
using Catalog.Domain.Movies;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Browse.GetPublishedMovie;

/// <summary>A published movie; anything else does not exist as far as customers can tell.</summary>
public sealed record GetPublishedMovieQuery(Guid MovieId) : IQuery<PublishedMovie>;

public sealed record PublishedMovie(
    Guid Id,
    string Title,
    string? OriginalTitle,
    string Synopsis,
    int ReleaseYear,
    AgeRating AgeRating,
    int RuntimeMinutes,
    IReadOnlyList<GenreReference> Genres,
    IReadOnlyList<CreditResponse> Credits);

internal sealed class GetPublishedMovieHandler(ICatalogReadModel readModel, ContentReader reader) : IQueryHandler<GetPublishedMovieQuery, PublishedMovie>
{
    public async Task<Result<PublishedMovie>> Handle(GetPublishedMovieQuery query, CancellationToken cancellationToken)
    {
        var movie = await readModel.Movies
            .FirstOrDefaultAsync(movie => movie.Id == query.MovieId && movie.Status == PublicationStatus.Published, cancellationToken)
            .ConfigureAwait(false);

        if (movie is null)
        {
            return MovieErrors.NotFound;
        }

        var names = await reader.LoadNamesAsync([movie], cancellationToken).ConfigureAwait(false);
        return new PublishedMovie(
            movie.Id,
            movie.Info.Title,
            movie.Info.OriginalTitle,
            movie.Info.Synopsis,
            movie.Info.ReleaseYear,
            movie.Info.AgeRating,
            movie.RuntimeMinutes,
            names.GenresOf(movie),
            names.CreditsOf(movie));
    }
}
