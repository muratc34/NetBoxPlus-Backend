using Catalog.Domain.Genres;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Genres.CreateGenre;

public sealed record CreateGenreCommand(string? Name) : ICommand<Guid>, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => null;
}

internal sealed class CreateGenreHandler(IGenreRepository genres) : ICommandHandler<CreateGenreCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateGenreCommand command, CancellationToken cancellationToken)
    {
        var genre = Genre.Create(command.Name);
        if (genre.IsFailure)
        {
            return genre.Error;
        }

        if (await genres.ExistsByNameAsync(genre.Value.Name, cancellationToken).ConfigureAwait(false))
        {
            return GenreErrors.NameTaken;
        }

        genres.Add(genre.Value);
        return genre.Value.Id;
    }
}
