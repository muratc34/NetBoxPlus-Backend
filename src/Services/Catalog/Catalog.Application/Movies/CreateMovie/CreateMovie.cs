using Catalog.Application.Content;
using Catalog.Domain.Content;
using Catalog.Domain.Movies;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Catalog.Application.Movies.CreateMovie;

public sealed record CreateMovieCommand(ContentFields Fields, int RuntimeMinutes) : ICommand<Guid>, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => null;
}

internal sealed class CreateMovieValidator : IValidator<CreateMovieCommand>
{
    public IEnumerable<ValidationFailure> Validate(CreateMovieCommand instance)
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

internal sealed class CreateMovieHandler(IMovieRepository movies, ContentEditor editor) : ICommandHandler<CreateMovieCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateMovieCommand command, CancellationToken cancellationToken)
    {
        var references = await editor.CheckReferencesAsync(command.Fields, cancellationToken).ConfigureAwait(false);
        if (references.IsFailure)
        {
            return references.Error;
        }

        var movie = Movie.Create(command.Fields.ToInfo(), command.Fields.ToTexts(), command.RuntimeMinutes);
        if (movie.IsFailure)
        {
            return movie.Error;
        }

        var lists = ContentEditor.ApplyLists(movie.Value, command.Fields);
        if (lists.IsFailure)
        {
            return lists.Error;
        }

        movies.Add(movie.Value);
        return movie.Value.Id;
    }
}
