using Sample.Contracts;
using Shared.Application.Cqrs;
using Shared.Application.Messaging;
using Shared.Domain;

namespace Feed.Application.Items.AddFeedItem;

/// <summary>
/// Adds every new note of the sample service to the feed. Goes through the dispatcher, so the
/// message is validated and saved by the same pipeline as an HTTP request; the inbox record is
/// committed in that save.
/// </summary>
internal sealed class NoteCreatedHandler(IDispatcher dispatcher) : IIntegrationEventHandler<NoteCreated>
{
    public Task<Result> Handle(NoteCreated integrationEvent, CancellationToken cancellationToken)
        => dispatcher.Send(new AddFeedItemCommand(integrationEvent.NoteId, integrationEvent.Title), cancellationToken);
}
