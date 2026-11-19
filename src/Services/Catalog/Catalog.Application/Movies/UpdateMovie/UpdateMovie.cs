using Catalog.Application.Content;
using Catalog.Domain.Content;
using Catalog.Domain.Movies;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Catalog.Application.Movies.UpdateMovie;

/// <param name="Version">The version the editor loaded.</param>
public sealed record UpdateMovieCommand(Guid MovieId, ContentFields Fields, int RuntimeMinutes, int Version) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => MovieId;
}

internal sealed class UpdateMovieValidator : IValidator<UpdateMovieCommand>
{
    public IEnumerable<ValidationFailure> Validate(UpdateMovieCommand instance)
    {
        foreach (var failure in instance.Fields.Validate())
        {
            yield return failure;
        }

        var runtime = Runtime.Check(instance.RuntimeMinutes);
        if (runtime.IsFailure)
        {
            yield return new ValidationFailure(nameof(instance.RuntimeMinutes), runtime.Error.Code, runtime.Error.Message);
        }
    }
}

internal sealed class UpdateMovieHandler(IMovieRepository movies, ContentEditor editor) : ICommandHandler<UpdateMovieCommand>
{
    public async Task<Result> Handle(UpdateMovieCommand command, CancellationToken cancellationToken)
    {
        var movie = await movies.GetByIdAsync(command.MovieId, cancellationToken).ConfigureAwait(false);
        if (movie is null)
        {
            return MovieErrors.NotFound;
        }

        var version = movie.CheckVersion(command.Version);
        if (version.IsFailure)
        {
            return version;
        }

        var references = await editor.CheckReferencesAsync(command.Fields, cancellationToken).ConfigureAwait(false);
        if (references.IsFailure)
        {
            return references;
        }

        movie.Describe(command.Fields.ToInfo());
        var texts = movie.SetTexts(command.Fields.ToTexts());
        if (texts.IsFailure)
        {
            return texts;
        }

        var runtime = movie.ChangeRuntime(command.RuntimeMinutes);
        return runtime.IsFailure ? runtime : ContentEditor.ApplyLists(movie, command.Fields);
    }
}
