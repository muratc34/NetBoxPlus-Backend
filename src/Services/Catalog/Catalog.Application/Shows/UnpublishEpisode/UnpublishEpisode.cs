using Catalog.Domain.Shows;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Shows.UnpublishEpisode;

/// <summary>Hides an episode.</summary>
/// <param name="Version">The version of the series the editor loaded.</param>
public sealed record UnpublishEpisodeCommand(Guid SeriesId, Guid EpisodeId, int Version) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => SeriesId;
}

internal sealed class UnpublishEpisodeHandler(ISeriesRepository repository) : ICommandHandler<UnpublishEpisodeCommand>
{
    public async Task<Result> Handle(UnpublishEpisodeCommand command, CancellationToken cancellationToken)
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

        return series.UnpublishEpisode(command.EpisodeId);
    }
}
