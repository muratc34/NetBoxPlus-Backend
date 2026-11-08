using Catalog.Domain.Shows;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Shows.DeleteSeries;

/// <summary>Soft-deletes it; published content must be unpublished first.</summary>
public sealed record DeleteSeriesCommand(Guid SeriesId) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => SeriesId;
}

internal sealed class DeleteSeriesHandler(ISeriesRepository repository) : ICommandHandler<DeleteSeriesCommand>
{
    public async Task<Result> Handle(DeleteSeriesCommand command, CancellationToken cancellationToken)
    {
        var series = await repository.GetByIdAsync(command.SeriesId, cancellationToken).ConfigureAwait(false);
        if (series is null)
        {
            return SeriesErrors.NotFound;
        }

        var allowed = series.CanDelete();
        if (allowed.IsFailure)
        {
            return allowed;
        }

        repository.Remove(series);
        return Result.Success();
    }
}
