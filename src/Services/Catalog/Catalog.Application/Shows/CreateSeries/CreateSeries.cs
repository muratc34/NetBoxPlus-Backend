using Catalog.Application.Content;
using Catalog.Domain.Shows;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Catalog.Application.Shows.CreateSeries;

public sealed record CreateSeriesCommand(ContentFields Fields) : ICommand<Guid>, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => null;
}

internal sealed class CreateSeriesValidator : IValidator<CreateSeriesCommand>
{
    public IEnumerable<ValidationFailure> Validate(CreateSeriesCommand instance) => instance.Fields.Validate();
}

internal sealed class CreateSeriesHandler(ISeriesRepository repository, ContentEditor editor) : ICommandHandler<CreateSeriesCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateSeriesCommand command, CancellationToken cancellationToken)
    {
        var references = await editor.CheckReferencesAsync(command.Fields, cancellationToken).ConfigureAwait(false);
        if (references.IsFailure)
        {
            return references.Error;
        }

        var created = Series.Create(command.Fields.ToInfo(), command.Fields.ToTexts());
        if (created.IsFailure)
        {
            return created.Error;
        }

        var series = created.Value;
        var lists = ContentEditor.ApplyLists(series, command.Fields);
        if (lists.IsFailure)
        {
            return lists.Error;
        }

        repository.Add(series);
        return series.Id;
    }
}
