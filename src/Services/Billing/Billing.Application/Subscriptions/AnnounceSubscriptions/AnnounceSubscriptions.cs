using Billing.Application.Abstractions;
using Billing.Contracts;
using Billing.Domain.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Application.Events;
using Shared.Domain;

namespace Billing.Application.Subscriptions.AnnounceSubscriptions;

/// <summary>
/// Publishes every subscription's state as it is now, with its current revision: fills a consumer
/// that started after the subscriptions changed. A consumer that already has that revision ignores
/// it. Subscriptions that never started (incomplete) are not announced. Run by the migrator on
/// request, not by people.
/// </summary>
public sealed record AnnounceSubscriptionsCommand : ICommand<int>;

internal sealed class AnnounceSubscriptionsHandler(IBillingReadModel readModel, IIntegrationEventPublisher publisher, TimeProvider timeProvider)
    : ICommandHandler<AnnounceSubscriptionsCommand, int>
{
    public async Task<Result<int>> Handle(AnnounceSubscriptionsCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var plans = await readModel.Plans.ToDictionaryAsync(plan => plan.Id, cancellationToken).ConfigureAwait(false);
        var subscriptions = await readModel.Subscriptions
            .Where(subscription => subscription.Status != SubscriptionStatus.Incomplete)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var subscription in subscriptions)
        {
            var publishing = subscription.Status switch
            {
                SubscriptionStatus.Active => publisher.PublishAsync(
                    new SubscriptionActivated(
                        subscription.Id,
                        subscription.UserId,
                        subscription.PlanId,
                        plans[subscription.PlanId].Quality.ToString(),
                        plans[subscription.PlanId].MaxScreens,
                        subscription.CurrentPeriodEnd,
                        subscription.Revision,
                        now),
                    cancellationToken),
                SubscriptionStatus.PastDue => publisher.PublishAsync(
                    new SubscriptionSuspended(subscription.Id, subscription.UserId, subscription.Revision, now), cancellationToken),
                _ => publisher.PublishAsync(
                    new SubscriptionCanceled(subscription.Id, subscription.UserId, subscription.CanceledAt ?? now, subscription.Revision, now), cancellationToken),
            };

            await publishing.ConfigureAwait(false);
        }

        return subscriptions.Count;
    }
}
