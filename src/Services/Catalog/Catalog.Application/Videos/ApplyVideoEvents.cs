using Catalog.Application.Videos.ApplyVideoState;
using Catalog.Domain.Content;
using Shared.Application.Cqrs;
using Shared.Application.Messaging;
using Shared.Domain;
using Streaming.Contracts;

namespace Catalog.Application.Videos;

/// <summary>
/// Follows the streaming service's video events. A video for content the catalog does not have
/// (deleted, or never there) is ignored. The full type name is this handler's inbox key: renaming
/// or moving it would process the messages it already handled again.
/// </summary>
internal sealed class ApplyVideoEvents(IDispatcher dispatcher)
    : IIntegrationEventHandler<VideoQueued>, IIntegrationEventHandler<VideoReady>, IIntegrationEventHandler<VideoFailed>
{
    public Task<Result> Handle(VideoQueued integrationEvent, CancellationToken cancellationToken)
        => dispatcher.Send(new ApplyVideoStateCommand(integrationEvent.ContentKind, integrationEvent.ContentId, integrationEvent.VideoId, VideoState.Processing, integrationEvent.Revision), cancellationToken);

    public Task<Result> Handle(VideoReady integrationEvent, CancellationToken cancellationToken)
        => dispatcher.Send(new ApplyVideoStateCommand(integrationEvent.ContentKind, integrationEvent.ContentId, integrationEvent.VideoId, VideoState.Ready, integrationEvent.Revision), cancellationToken);

    public Task<Result> Handle(VideoFailed integrationEvent, CancellationToken cancellationToken)
        => dispatcher.Send(new ApplyVideoStateCommand(integrationEvent.ContentKind, integrationEvent.ContentId, integrationEvent.VideoId, VideoState.Failed, integrationEvent.Revision), cancellationToken);
}
