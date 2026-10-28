using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Shared.Domain;

namespace Shared.Application.Events;

/// <summary>
/// Calls every handler of each event, in order. Events without handlers are allowed.
/// </summary>
internal sealed class DomainEventDispatcher(IServiceProvider services) : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, DomainEventHandlerWrapper> Wrappers = new();

    public async Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);

        foreach (var domainEvent in domainEvents)
        {
            var wrapper = Wrappers.GetOrAdd(
                domainEvent.GetType(),
                static type => (DomainEventHandlerWrapper)Activator.CreateInstance(
                    typeof(DomainEventHandlerWrapper<>).MakeGenericType(type))!);

            await wrapper.Handle(domainEvent, services, cancellationToken).ConfigureAwait(false);
        }
    }
}

internal abstract class DomainEventHandlerWrapper
{
    public abstract Task Handle(IDomainEvent domainEvent, IServiceProvider services, CancellationToken cancellationToken);
}

internal sealed class DomainEventHandlerWrapper<TEvent> : DomainEventHandlerWrapper
    where TEvent : IDomainEvent
{
    public override async Task Handle(IDomainEvent domainEvent, IServiceProvider services, CancellationToken cancellationToken)
    {
        foreach (var handler in services.GetServices<IDomainEventHandler<TEvent>>())
        {
            await handler.Handle((TEvent)domainEvent, cancellationToken).ConfigureAwait(false);
        }
    }
}
