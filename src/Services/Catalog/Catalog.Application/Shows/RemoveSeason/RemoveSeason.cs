using Catalog.Domain.Shows;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Shows.RemoveSeason;

/// <summary>Removes a season without published episodes.</summary>
/// <param name="Version">The version of the series the editor loaded.</param>
public sealed record RemoveSeasonCommand(Guid SeriesId, Guid SeasonId, int Version) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => SeriesId;
}

internal sealed class RemoveSeasonHandler(ISeriesRepository repository) : ICommandHandler<RemoveSeasonCommand>
{
    public async Task<Result> Handle(RemoveSeasonCommand command, CancellationToken cancellationToken)
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

        return series.RemoveSeason(command.SeasonId);
    }
}
