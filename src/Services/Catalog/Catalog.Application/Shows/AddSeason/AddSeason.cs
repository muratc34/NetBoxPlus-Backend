using Catalog.Domain.Shows;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Shows.AddSeason;

/// <summary>Adds a season to the series.</summary>
/// <param name="Version">The version of the series the editor loaded.</param>
public sealed record AddSeasonCommand(Guid SeriesId, int Number, string? Title, int Version) : ICommand<Guid>, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => SeriesId;
}

internal sealed class AddSeasonHandler(ISeriesRepository repository) : ICommandHandler<AddSeasonCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AddSeasonCommand command, CancellationToken cancellationToken)
    {
        var series = await repository.GetByIdAsync(command.SeriesId, cancellationToken).ConfigureAwait(false);
        if (series is null)
        {
            return SeriesErrors.NotFound;
        }

        var version = series.CheckVersion(command.Version);
        if (version.IsFailure)
        {
            return version.Error;
        }

        var season = series.AddSeason(command.Number, command.Title);
        return season.IsFailure ? season.Error : season.Value.Id;
    }
}
