using Catalog.Domain.Movies;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Movies.PublishMovie;

/// <summary>Makes it visible to customers.</summary>
public sealed record PublishMovieCommand(Guid MovieId) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => MovieId;
}

internal sealed class PublishMovieHandler(IMovieRepository repository, TimeProvider timeProvider) : ICommandHandler<PublishMovieCommand>
{
    public async Task<Result> Handle(PublishMovieCommand command, CancellationToken cancellationToken)
    {
        var movie = await repository.GetByIdAsync(command.MovieId, cancellationToken).ConfigureAwait(false);
        if (movie is null)
        {
            return MovieErrors.NotFound;
        }

        return movie.Publish(timeProvider.GetUtcNow());
    }
}
