using Catalog.Domain.Shows;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Shows.UpdateSeason;

/// <summary>Changes the number and title of a season.</summary>
/// <param name="Version">The version of the series the editor loaded.</param>
public sealed record UpdateSeasonCommand(Guid SeriesId, Guid SeasonId, int Number, IReadOnlyDictionary<string, string?>? Titles, int Version) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => SeriesId;
}

internal sealed class UpdateSeasonHandler(ISeriesRepository repository) : ICommandHandler<UpdateSeasonCommand>
{
    public async Task<Result> Handle(UpdateSeasonCommand command, CancellationToken cancellationToken)
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

        return series.ChangeSeason(command.SeasonId, command.Number, command.Titles);
    }
}
