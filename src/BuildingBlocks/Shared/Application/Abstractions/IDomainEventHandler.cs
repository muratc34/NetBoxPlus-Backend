using MediatR;
using Shared.Domain.Event;

namespace Shared.Application.Abstractions;

public interface IDomainEventHandler<in TDomainEvent> : INotificationHandler<TDomainEvent>
        where TDomainEvent : IDomainEvent
{
}
