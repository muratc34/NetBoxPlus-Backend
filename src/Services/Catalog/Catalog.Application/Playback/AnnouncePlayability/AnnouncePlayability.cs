using Catalog.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Application.Events;
using Shared.Domain;

namespace Catalog.Application.Playback.AnnouncePlayability;

/// <summary>
/// Publishes what can be watched of every title as it is now, with its current revision: fills a
/// consumer that started after the titles were published. A consumer that already has that
/// revision ignores it. Run by the migrator on request, not by people.
/// </summary>
public sealed record AnnouncePlayabilityCommand : ICommand<int>;

internal sealed class AnnouncePlayabilityHandler(ICatalogReadModel readModel, IIntegrationEventPublisher publisher, TimeProvider timeProvider)
    : ICommandHandler<AnnouncePlayabilityCommand, int>
{
    public async Task<Result<int>> Handle(AnnouncePlayabilityCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var movies = await readModel.Movies.ToListAsync(cancellationToken).ConfigureAwait(false);
        var series = await readModel.WithSeasons(readModel.Series).ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var title in movies.Cast<Domain.Content.ContentItem>().Concat(series))
        {
            await publisher.PublishAsync(PlayableEvents.From(title.Playability(), now), cancellationToken).ConfigureAwait(false);
        }

        return movies.Count + series.Count;
    }
}
