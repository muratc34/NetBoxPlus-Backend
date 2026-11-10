using Billing.Contracts;
using Billing.Domain.Invoices;
using Billing.Domain.Plans;
using Billing.Domain.Subscriptions;
using Shared.Application.Events;

namespace Billing.Application.Subscriptions;

// These run while the change is being saved: the outbox message commits with the change itself,
// so no event goes out for a change that was not kept.

internal sealed class PublishSubscriptionActivated(IIntegrationEventPublisher publisher, IPlanRepository plans, TimeProvider timeProvider)
    : IDomainEventHandler<SubscriptionActivatedDomainEvent>
{
    public async Task Handle(SubscriptionActivatedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        var plan = await plans.GetByIdAsync(domainEvent.PlanId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Plan {domainEvent.PlanId} of subscription {domainEvent.SubscriptionId} does not exist.");

        await publisher.PublishAsync(
            new SubscriptionActivated(
                domainEvent.SubscriptionId,
                domainEvent.UserId,
                plan.Id,
                plan.Quality.ToString(),
                plan.MaxScreens,
                domainEvent.CurrentPeriodEnd,
                domainEvent.Revision,
                timeProvider.GetUtcNow()),
            cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class PublishSubscriptionSuspended(IIntegrationEventPublisher publisher, TimeProvider timeProvider)
    : IDomainEventHandler<SubscriptionSuspendedDomainEvent>
{
    public Task Handle(SubscriptionSuspendedDomainEvent domainEvent, CancellationToken cancellationToken)
        => publisher.PublishAsync(
            new SubscriptionSuspended(domainEvent.SubscriptionId, domainEvent.UserId, domainEvent.Revision, timeProvider.GetUtcNow()),
            cancellationToken);
}

internal sealed class PublishSubscriptionCanceled(IIntegrationEventPublisher publisher, TimeProvider timeProvider)
    : IDomainEventHandler<SubscriptionCanceledDomainEvent>
{
    public Task Handle(SubscriptionCanceledDomainEvent domainEvent, CancellationToken cancellationToken)
        => publisher.PublishAsync(
            new SubscriptionCanceled(domainEvent.SubscriptionId, domainEvent.UserId, domainEvent.CanceledAt, domainEvent.Revision, timeProvider.GetUtcNow()),
            cancellationToken);
}

internal sealed class PublishPaymentFailed(IIntegrationEventPublisher publisher, TimeProvider timeProvider)
    : IDomainEventHandler<InvoicePaymentFailedDomainEvent>
{
    public Task Handle(InvoicePaymentFailedDomainEvent domainEvent, CancellationToken cancellationToken)
        => publisher.PublishAsync(
            new PaymentFailed(domainEvent.InvoiceId, domainEvent.SubscriptionId, domainEvent.UserId, domainEvent.AmountMinor, domainEvent.Currency, timeProvider.GetUtcNow()),
            cancellationToken);
}
