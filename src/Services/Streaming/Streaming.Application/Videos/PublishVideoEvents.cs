using Shared.Application.Events;
using Streaming.Contracts;
using Streaming.Domain.Videos;

namespace Streaming.Application.Videos;

// Run while the change is being saved: the outbox message commits with the change itself.

internal sealed class PublishVideoQueued(IIntegrationEventPublisher publisher, TimeProvider timeProvider) : IDomainEventHandler<VideoQueuedDomainEvent>
{
    public Task Handle(VideoQueuedDomainEvent domainEvent, CancellationToken cancellationToken)
        => publisher.PublishAsync(
            new VideoQueued(domainEvent.VideoId, domainEvent.ContentKind.ToString(), domainEvent.ContentId, domainEvent.Revision, timeProvider.GetUtcNow()),
            cancellationToken);
}

internal sealed class PublishVideoReady(IIntegrationEventPublisher publisher, TimeProvider timeProvider) : IDomainEventHandler<VideoReadyDomainEvent>
{
    public Task Handle(VideoReadyDomainEvent domainEvent, CancellationToken cancellationToken)
        => publisher.PublishAsync(
            new VideoReady(domainEvent.VideoId, domainEvent.ContentKind.ToString(), domainEvent.ContentId, domainEvent.DurationSeconds, domainEvent.Revision, timeProvider.GetUtcNow()),
            cancellationToken);
}

internal sealed class PublishVideoFailed(IIntegrationEventPublisher publisher, TimeProvider timeProvider) : IDomainEventHandler<VideoFailedDomainEvent>
{
    public Task Handle(VideoFailedDomainEvent domainEvent, CancellationToken cancellationToken)
        => publisher.PublishAsync(
            new VideoFailed(domainEvent.VideoId, domainEvent.ContentKind.ToString(), domainEvent.ContentId, domainEvent.Reason, domainEvent.Revision, timeProvider.GetUtcNow()),
            cancellationToken);
}
