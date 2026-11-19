using Catalog.Domain.Genres;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Genres.CreateGenre;

public sealed record CreateGenreCommand(IReadOnlyDictionary<string, string?> Names) : ICommand<Guid>, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => null;
}

internal sealed class CreateGenreHandler(IGenreRepository genres) : ICommandHandler<CreateGenreCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateGenreCommand command, CancellationToken cancellationToken)
    {
        var genre = Genre.Create(command.Names);
        if (genre.IsFailure)
        {
            return genre.Error;
        }

        if (await genres.AnyNameTakenAsync(genre.Value, cancellationToken).ConfigureAwait(false))
        {
            return GenreErrors.NameTaken;
        }

        genres.Add(genre.Value);
        return genre.Value.Id;
    }
}
