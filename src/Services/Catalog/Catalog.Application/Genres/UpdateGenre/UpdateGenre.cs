using Catalog.Domain.Genres;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Genres.UpdateGenre;

public sealed record UpdateGenreCommand(Guid GenreId, IReadOnlyDictionary<string, string?> Names) : ICommand, IAuditedCommand
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

        var renamed = genre.Rename(command.Names);
        if (renamed.IsFailure)
        {
            return renamed;
        }

        return await genres.AnyNameTakenAsync(genre, cancellationToken).ConfigureAwait(false)
            ? GenreErrors.NameTaken
            : Result.Success();
    }
}
