using Catalog.Domain.Shows;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Shows.UpdateEpisode;

/// <summary>Changes an episode.</summary>
/// <param name="Version">The version of the series the editor loaded.</param>
public sealed record UpdateEpisodeCommand(Guid SeriesId, Guid EpisodeId, int Number, string? Title, string? Synopsis, int RuntimeMinutes, int Version) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => SeriesId;
}

internal sealed class UpdateEpisodeHandler(ISeriesRepository repository) : ICommandHandler<UpdateEpisodeCommand>
{
    public async Task<Result> Handle(UpdateEpisodeCommand command, CancellationToken cancellationToken)
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

        var details = EpisodeDetails.Create(command.Number, command.Title, command.Synopsis, command.RuntimeMinutes);
        if (details.IsFailure)
        {
            return details.Error;
        }

        return series.ChangeEpisode(command.EpisodeId, details.Value);
    }
}
