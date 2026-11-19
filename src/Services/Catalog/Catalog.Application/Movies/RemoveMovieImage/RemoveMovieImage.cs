using Catalog.Application.Images;
using Catalog.Domain.Content;
using Catalog.Domain.Movies;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Movies.RemoveMovieImage;

/// <summary>Empties an image slot.</summary>
/// <param name="Version">The version the editor loaded.</param>
public sealed record RemoveMovieImageCommand(Guid MovieId, ContentImageSlot Slot, int Version) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => MovieId;
}

internal sealed class RemoveMovieImageHandler(IMovieRepository repository, ImageAttacher attacher) : ICommandHandler<RemoveMovieImageCommand>
{
    public async Task<Result> Handle(RemoveMovieImageCommand command, CancellationToken cancellationToken)
    {
        var movie = await repository.GetByIdAsync(command.MovieId, cancellationToken).ConfigureAwait(false);
        if (movie is null)
        {
            return MovieErrors.NotFound;
        }

        var version = movie.CheckVersion(command.Version);
        if (version.IsFailure)
        {
            return version;
        }

        var previous = movie.ImageOf(command.Slot);
        var removed = movie.RemoveImage(command.Slot);
        if (removed.IsFailure)
        {
            return removed;
        }

        await attacher.RetireAsync(previous, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
