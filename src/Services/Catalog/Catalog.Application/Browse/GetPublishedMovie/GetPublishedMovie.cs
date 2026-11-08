using Catalog.Application.Abstractions;
using Catalog.Application.Content;
using Catalog.Domain.Content;
using Catalog.Domain.Movies;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Catalog.Application.Browse.GetPublishedMovie;

/// <summary>A published movie; anything else does not exist as far as customers can tell.</summary>
public sealed record GetPublishedMovieQuery(Guid MovieId, string Language) : IQuery<PublishedMovie>;

public sealed record PublishedMovie(
    Guid Id,
    string Title,
    string Language,
    string? OriginalTitle,
    string Synopsis,
    int ReleaseYear,
    AgeRating AgeRating,
    int RuntimeMinutes,
    Uri? PosterUrl,
    Uri? BackdropUrl,
    IReadOnlyList<GenreReference> Genres,
    IReadOnlyList<CreditResponse> Credits);

internal sealed class GetPublishedMovieValidator : IValidator<GetPublishedMovieQuery>
{
    public IEnumerable<ValidationFailure> Validate(GetPublishedMovieQuery instance) => LanguageValidation.Validate(instance.Language);
}

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
        var text = movie.TextFor(query.Language);
        return new PublishedMovie(
            movie.Id,
            text.Title,
            text.Language,
            movie.Info.OriginalTitle,
            text.Synopsis,
            movie.Info.ReleaseYear,
            movie.Info.AgeRating,
            movie.RuntimeMinutes,
            names.ImageAddress(movie.PosterImage),
            names.ImageAddress(movie.BackdropImage),
            names.GenresOf(movie, query.Language),
            names.CreditsOf(movie));
    }
}
