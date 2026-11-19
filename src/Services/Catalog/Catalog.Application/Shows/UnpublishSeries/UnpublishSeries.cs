using Catalog.Domain.Shows;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Shows.UnpublishSeries;

/// <summary>Hides it from customers.</summary>
public sealed record UnpublishSeriesCommand(Guid SeriesId) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => SeriesId;
}

internal sealed class UnpublishSeriesHandler(ISeriesRepository repository) : ICommandHandler<UnpublishSeriesCommand>
{
    public async Task<Result> Handle(UnpublishSeriesCommand command, CancellationToken cancellationToken)
    {
        var series = await repository.GetByIdAsync(command.SeriesId, cancellationToken).ConfigureAwait(false);
        if (series is null)
        {
            return SeriesErrors.NotFound;
        }

        series.Unpublish();
        return Result.Success();
    }
}
