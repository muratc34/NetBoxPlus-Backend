using Catalog.Domain.Movies;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Movies.UnpublishMovie;

/// <summary>Hides it from customers.</summary>
public sealed record UnpublishMovieCommand(Guid MovieId) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => MovieId;
}

internal sealed class UnpublishMovieHandler(IMovieRepository repository) : ICommandHandler<UnpublishMovieCommand>
{
    public async Task<Result> Handle(UnpublishMovieCommand command, CancellationToken cancellationToken)
    {
        var movie = await repository.GetByIdAsync(command.MovieId, cancellationToken).ConfigureAwait(false);
        if (movie is null)
        {
            return MovieErrors.NotFound;
        }

        movie.Unpublish();
        return Result.Success();
    }
}
