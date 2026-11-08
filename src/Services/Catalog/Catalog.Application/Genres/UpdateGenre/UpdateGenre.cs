using Catalog.Domain.Genres;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Genres.UpdateGenre;

public sealed record UpdateGenreCommand(Guid GenreId, string? Name) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => GenreId;
}

internal sealed class UpdateGenreHandler(IGenreRepository genres) : ICommandHandler<UpdateGenreCommand>
{
    public async Task<Result> Handle(UpdateGenreCommand command, CancellationToken cancellationToken)
    {
        var genre = await genres.GetByIdAsync(command.GenreId, cancellationToken).ConfigureAwait(false);
        if (genre is null)
        {
            return GenreErrors.NotFound;
        }

        var previous = genre.NormalizedName;
        var renamed = genre.Rename(command.Name);
        if (renamed.IsFailure)
        {
            return renamed;
        }

        return genre.NormalizedName != previous && await genres.ExistsByNameAsync(genre.Name, cancellationToken).ConfigureAwait(false)
            ? GenreErrors.NameTaken
            : Result.Success();
    }
}
