using Catalog.Domain.Shows;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Shows.RemoveEpisode;

/// <summary>Removes an unpublished episode.</summary>
/// <param name="Version">The version of the series the editor loaded.</param>
public sealed record RemoveEpisodeCommand(Guid SeriesId, Guid EpisodeId, int Version) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => SeriesId;
}

internal sealed class RemoveEpisodeHandler(ISeriesRepository repository) : ICommandHandler<RemoveEpisodeCommand>
{
    public async Task<Result> Handle(RemoveEpisodeCommand command, CancellationToken cancellationToken)
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

        return series.RemoveEpisode(command.EpisodeId);
    }
}
