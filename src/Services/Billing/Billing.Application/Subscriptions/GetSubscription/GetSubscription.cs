using Billing.Application.Abstractions;
using Billing.Domain.Plans;
using Billing.Domain.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Billing.Application.Subscriptions.GetSubscription;

public sealed record GetSubscriptionQuery(Guid SubscriptionId) : IQuery<SubscriptionDetails>;

/// <param name="ProviderSubscriptionId">For looking it up at the payment provider.</param>
public sealed record SubscriptionDetails(
    Guid Id,
    Guid UserId,
    string? Email,
    PlanReference Plan,
    SubscriptionStatus Status,
    string ProviderSubscriptionId,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    DateTimeOffset? CanceledAt,
    int Revision,
    DateTimeOffset CreatedAt,
    IReadOnlyList<InvoiceSummary> Invoices);

public static class SubscriptionQueryErrors
{
    public static readonly Error NotFound = Error.NotFound("Subscription.NotFound", "The subscription was not found.");

    public static readonly Error NoneYet = Error.NotFound("Subscription.None", "No subscription has been started yet.");
}

internal sealed class GetSubscriptionHandler(IBillingReadModel readModel) : IQueryHandler<GetSubscriptionQuery, SubscriptionDetails>
{
    public async Task<Result<SubscriptionDetails>> Handle(GetSubscriptionQuery query, CancellationToken cancellationToken)
    {
        var subscription = await readModel.Subscriptions.FirstOrDefaultAsync(item => item.Id == query.SubscriptionId, cancellationToken).ConfigureAwait(false);
        if (subscription is null)
        {
            return SubscriptionQueryErrors.NotFound;
        }

        var plan = await readModel.Plans.SingleAsync(item => item.Id == subscription.PlanId, cancellationToken).ConfigureAwait(false);
        var email = await readModel.Customers
            .Where(customer => customer.UserId == subscription.UserId)
            .Select(customer => customer.Email)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var invoices = await readModel.Invoices
            .Where(invoice => invoice.SubscriptionId == subscription.Id)
            .OrderByDescending(invoice => invoice.IssuedAt)
            .ThenByDescending(invoice => invoice.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new SubscriptionDetails(
            subscription.Id,
            subscription.UserId,
            email,
            plan.ToReference(Languages.Default),
            subscription.Status,
            subscription.ProviderSubscriptionId,
            subscription.CurrentPeriodEnd,
            subscription.CancelAtPeriodEnd,
            subscription.CanceledAt,
            subscription.Revision,
            subscription.CreatedAt,
            [.. invoices.Select(invoice => invoice.ToSummary())]);
    }
}
