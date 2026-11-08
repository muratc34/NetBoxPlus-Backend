using Catalog.Application.Content;
using Catalog.Domain.Shows;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Catalog.Application.Shows.UpdateSeries;

/// <param name="Version">The version the editor loaded.</param>
public sealed record UpdateSeriesCommand(Guid SeriesId, ContentFields Fields, int Version) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => SeriesId;
}

internal sealed class UpdateSeriesValidator : IValidator<UpdateSeriesCommand>
{
    public IEnumerable<ValidationFailure> Validate(UpdateSeriesCommand instance) => instance.Fields.Validate();
}

internal sealed class UpdateSeriesHandler(ISeriesRepository repository, ContentEditor editor) : ICommandHandler<UpdateSeriesCommand>
{
    public async Task<Result> Handle(UpdateSeriesCommand command, CancellationToken cancellationToken)
    {
        var series = await repository.GetByIdAsync(command.SeriesId, cancellationToken).ConfigureAwait(false);
        if (series is null)
        {
            return SeriesErrors.NotFound;
        }

        var version = series.CheckVersion(command.Version);
        if (version.IsFailure)
        {
            return version;
        }

        var references = await editor.CheckReferencesAsync(command.Fields, cancellationToken).ConfigureAwait(false);
        if (references.IsFailure)
        {
            return references;
        }

        series.Describe(command.Fields.ToInfo());
        return ContentEditor.ApplyLists(series, command.Fields);
    }
}
