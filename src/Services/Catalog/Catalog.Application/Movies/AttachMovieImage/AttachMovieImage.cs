using Catalog.Application.Images;
using Catalog.Domain.Content;
using Catalog.Domain.Images;
using Catalog.Domain.Movies;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Movies.AttachMovieImage;

/// <summary>Puts an uploaded image in a slot, replacing what was there.</summary>
/// <param name="Version">The version the editor loaded.</param>
public sealed record AttachMovieImageCommand(Guid MovieId, ContentImageSlot Slot, Guid UploadId, int Version) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => MovieId;
}

internal sealed class AttachMovieImageHandler(IMovieRepository repository, ImageAttacher attacher) : ICommandHandler<AttachMovieImageCommand>
{
    public async Task<Result> Handle(AttachMovieImageCommand command, CancellationToken cancellationToken)
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

        var use = command.Slot == ContentImageSlot.Poster ? ImageUse.Poster : ImageUse.Backdrop;
        var image = await attacher.ConsumeAsync(command.UploadId, use, cancellationToken).ConfigureAwait(false);
        if (image.IsFailure)
        {
            return image.Error;
        }

        await attacher.RetireAsync(movie.ImageOf(command.Slot), cancellationToken).ConfigureAwait(false);
        movie.SetImage(command.Slot, image.Value);
        return Result.Success();
    }
}
