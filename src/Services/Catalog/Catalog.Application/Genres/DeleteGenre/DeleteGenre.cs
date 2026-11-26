using Catalog.Application.Abstractions;
using Catalog.Domain.Genres;
using Catalog.Domain.Movies;
using Catalog.Domain.Shows;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Genres.DeleteGenre;

/// <summary>
/// Deletes a genre that no movie or series uses. Deleted content does not count: it lets go of the genre in
/// the same transaction (its links would hold the genre's row).
/// </summary>
public sealed record DeleteGenreCommand(Guid GenreId) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => GenreId;
}

internal sealed class DeleteGenreHandler(IGenreRepository genres, IMovieRepository movies, ISeriesRepository series, ICatalogReadModel readModel)
    : ICommandHandler<DeleteGenreCommand>
{
    public async Task<Result> Handle(DeleteGenreCommand command, CancellationToken cancellationToken)
    {
        var genre = await genres.GetByIdAsync(command.GenreId, cancellationToken).ConfigureAwait(false);
        if (genre is null)
        {
            return GenreErrors.NotFound;
        }

        var inUse = await readModel.Movies.AnyAsync(movie => movie.Genres.Any(item => item.GenreId == command.GenreId), cancellationToken).ConfigureAwait(false)
            || await readModel.Series.AnyAsync(show => show.Genres.Any(item => item.GenreId == command.GenreId), cancellationToken).ConfigureAwait(false);
        if (inUse)
        {
            return GenreErrors.InUse;
        }

        foreach (var movie in await movies.ListDeletedUsingAsync(command.GenreId, personId: null, cancellationToken).ConfigureAwait(false))
        {
            movie.ReleaseGenre(command.GenreId);
        }

        foreach (var show in await series.ListDeletedUsingAsync(command.GenreId, personId: null, cancellationToken).ConfigureAwait(false))
        {
            show.ReleaseGenre(command.GenreId);
        }

        genres.Remove(genre);
        return Result.Success();
    }
}
