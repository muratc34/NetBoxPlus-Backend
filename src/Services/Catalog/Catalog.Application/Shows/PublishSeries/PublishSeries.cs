using Catalog.Domain.Shows;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Shows.PublishSeries;

/// <summary>Makes it visible to customers.</summary>
public sealed record PublishSeriesCommand(Guid SeriesId) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => SeriesId;
}

internal sealed class PublishSeriesHandler(ISeriesRepository repository, TimeProvider timeProvider) : ICommandHandler<PublishSeriesCommand>
{
    public async Task<Result> Handle(PublishSeriesCommand command, CancellationToken cancellationToken)
    {
        var series = await repository.GetByIdAsync(command.SeriesId, cancellationToken).ConfigureAwait(false);
        if (series is null)
        {
            return SeriesErrors.NotFound;
        }

        return series.Publish(timeProvider.GetUtcNow());
    }
}
