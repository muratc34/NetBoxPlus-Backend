using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shared.Application.Events;
using Shared.Domain;

namespace Shared.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Dispatches the domain events of tracked aggregates just before saving, so that whatever the
/// handlers change is written in the same transaction. Handlers may raise further events; they are
/// dispatched in further rounds.
/// </summary>
internal sealed class DomainEventsInterceptor(IDomainEventDispatcher dispatcher) : SaveChangesInterceptor
{
    private const int MaxRounds = 10;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            await DispatchAsync(context, cancellationToken).ConfigureAwait(false);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context && CollectAggregatesWithEvents(context).Count > 0)
        {
            throw new NotSupportedException("Aggregates with domain events must be saved with SaveChangesAsync.");
        }

        return base.SavingChanges(eventData, result);
    }

    private async Task DispatchAsync(DbContext context, CancellationToken cancellationToken)
    {
        for (var round = 0; round < MaxRounds; round++)
        {
            var aggregates = CollectAggregatesWithEvents(context);
            if (aggregates.Count == 0)
            {
                return;
            }

            // Copy, then clear, so that events raised by the handlers start a new round.
            var domainEvents = aggregates.SelectMany(aggregate => aggregate.DomainEvents).ToList();
            aggregates.ForEach(aggregate => aggregate.ClearDomainEvents());

            await dispatcher.DispatchAsync(domainEvents, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            $"Domain event handlers were still raising events after {MaxRounds} rounds. Check the handlers for a cycle.");
    }

    private static List<AggregateRoot> CollectAggregatesWithEvents(DbContext context)
    {
        context.ChangeTracker.DetectChanges();

        return context.ChangeTracker.Entries<AggregateRoot>()
            .Select(entry => entry.Entity)
            .Where(aggregate => aggregate.DomainEvents.Count > 0)
            .ToList();
    }
}
