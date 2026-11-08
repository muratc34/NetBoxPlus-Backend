using Catalog.Domain.Movies;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Movies.DeleteMovie;

/// <summary>Soft-deletes it; published content must be unpublished first.</summary>
public sealed record DeleteMovieCommand(Guid MovieId) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => MovieId;
}

internal sealed class DeleteMovieHandler(IMovieRepository repository) : ICommandHandler<DeleteMovieCommand>
{
    public async Task<Result> Handle(DeleteMovieCommand command, CancellationToken cancellationToken)
    {
        var movie = await repository.GetByIdAsync(command.MovieId, cancellationToken).ConfigureAwait(false);
        if (movie is null)
        {
            return MovieErrors.NotFound;
        }

        var allowed = movie.CanDelete();
        if (allowed.IsFailure)
        {
            return allowed;
        }

        repository.Remove(movie);
        return Result.Success();
    }
}
