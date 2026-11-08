using Catalog.Domain.Shows;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Shows.PublishEpisode;

/// <summary>Makes an episode visible.</summary>
/// <param name="Version">The version of the series the editor loaded.</param>
public sealed record PublishEpisodeCommand(Guid SeriesId, Guid EpisodeId, int Version) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => SeriesId;
}

internal sealed class PublishEpisodeHandler(ISeriesRepository repository, TimeProvider timeProvider) : ICommandHandler<PublishEpisodeCommand>
{
    public async Task<Result> Handle(PublishEpisodeCommand command, CancellationToken cancellationToken)
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

        return series.PublishEpisode(command.EpisodeId, timeProvider.GetUtcNow());
    }
}
