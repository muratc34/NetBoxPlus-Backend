using Catalog.Contracts;
using Catalog.Domain.Content;
using Shared.Application.Events;

namespace Catalog.Application.Playback;

/// <summary>Runs while the change is being saved: the outbox message commits with the change itself.</summary>
internal sealed class PublishPlayability(IIntegrationEventPublisher publisher, TimeProvider timeProvider)
    : IDomainEventHandler<PlayabilityChangedDomainEvent>
{
    public Task Handle(PlayabilityChangedDomainEvent domainEvent, CancellationToken cancellationToken)
        => publisher.PublishAsync(PlayableEvents.From(domainEvent.Title.Playability(), timeProvider.GetUtcNow()), cancellationToken);
}

internal static class PlayableEvents
{
    public static PlayableChanged From(Playability playability, DateTimeOffset now)
        => new(
            playability.TitleId,
            playability.Kind.ToString(),
            playability.AgeRating.ToString(),
            playability.Videos.Select(video => new PlayableItem(video.ContentId, video.VideoId)).ToList(),
            playability.Revision,
            now);
}
