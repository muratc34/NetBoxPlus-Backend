using Catalog.Application.Content;
using Catalog.Domain.Shows;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Shows.AddEpisode;

/// <summary>Adds an episode, hidden until it is published.</summary>
/// <param name="Version">The version of the series the editor loaded.</param>
public sealed record AddEpisodeCommand(Guid SeriesId, Guid SeasonId, int Number, IReadOnlyDictionary<string, ContentTextFields> Translations, int RuntimeMinutes, int Version) : ICommand<Guid>, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => SeriesId;
}

internal sealed class AddEpisodeHandler(ISeriesRepository repository) : ICommandHandler<AddEpisodeCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AddEpisodeCommand command, CancellationToken cancellationToken)
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

        var texts = ContentTextFields.ToTexts(command.Translations);
        if (texts.IsFailure)
        {
            return texts.Error;
        }

        var details = EpisodeDetails.Create(command.Number, texts.Value, command.RuntimeMinutes);
        if (details.IsFailure)
        {
            return details.Error;
        }

        var episode = series.AddEpisode(command.SeasonId, details.Value);
        return episode.IsFailure ? episode.Error : episode.Value.Id;
    }
}
