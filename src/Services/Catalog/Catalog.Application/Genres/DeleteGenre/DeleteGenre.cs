using Catalog.Application.Abstractions;
using Catalog.Domain.Genres;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Genres.DeleteGenre;

/// <summary>Deletes a genre that no movie or series uses, deleted content included.</summary>
public sealed record DeleteGenreCommand(Guid GenreId) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => GenreId;
}

internal sealed class DeleteGenreHandler(IGenreRepository genres, ICatalogReadModel readModel) : ICommandHandler<DeleteGenreCommand>
{
    public async Task<Result> Handle(DeleteGenreCommand command, CancellationToken cancellationToken)
    {
        var genre = await genres.GetByIdAsync(command.GenreId, cancellationToken).ConfigureAwait(false);
        if (genre is null)
        {
            return GenreErrors.NotFound;
        }

        var inUse = await readModel.MoviesIncludingDeleted.AnyAsync(movie => movie.Genres.Any(item => item.GenreId == command.GenreId), cancellationToken).ConfigureAwait(false)
            || await readModel.SeriesIncludingDeleted.AnyAsync(series => series.Genres.Any(item => item.GenreId == command.GenreId), cancellationToken).ConfigureAwait(false);
        if (inUse)
        {
            return GenreErrors.InUse;
        }

        genres.Remove(genre);
        return Result.Success();
    }
}
