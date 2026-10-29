using Sample.Contracts;
using Sample.Domain.Notes;
using Shared.Application.Events;

namespace Sample.Application.Notes.CreateNote;

/// <summary>
/// Tells other services about a new note. Runs while the note is being saved, so the outbox
/// message is committed in the same transaction as the note itself.
/// </summary>
internal sealed class PublishNoteCreated(IIntegrationEventPublisher publisher) : IDomainEventHandler<NoteCreatedDomainEvent>
{
    public Task Handle(NoteCreatedDomainEvent domainEvent, CancellationToken cancellationToken)
        => publisher.PublishAsync(new NoteCreated(domainEvent.NoteId, domainEvent.Title), cancellationToken);
}
