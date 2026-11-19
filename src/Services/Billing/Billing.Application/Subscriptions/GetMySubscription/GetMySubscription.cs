using Billing.Application.Abstractions;
using Billing.Application.Plans;
using Billing.Application.Subscriptions.GetSubscription;
using Billing.Domain.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Billing.Application.Subscriptions.GetMySubscription;

/// <summary>The caller's current subscription: the live one, or else the latest that ended.</summary>
public sealed record GetMySubscriptionQuery(Guid UserId, string Language) : IQuery<MySubscription>;

/// <param name="CancelAtPeriodEnd">Canceled by the customer, ending when the paid period does.</param>
public sealed record MySubscription(
    Guid Id,
    SubscriptionStatus Status,
    PlanReference Plan,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    DateTimeOffset? CanceledAt,
    DateTimeOffset StartedAt);

internal sealed class GetMySubscriptionValidator : IValidator<GetMySubscriptionQuery>
{
    public IEnumerable<ValidationFailure> Validate(GetMySubscriptionQuery instance) => LanguageValidation.Validate(instance.Language);
}

internal sealed class GetMySubscriptionHandler(IBillingReadModel readModel) : IQueryHandler<GetMySubscriptionQuery, MySubscription>
{
    public async Task<Result<MySubscription>> Handle(GetMySubscriptionQuery query, CancellationToken cancellationToken)
    {
        var subscription = await readModel.Subscriptions
            .Where(item => item.UserId == query.UserId)
            .CurrentFirst()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (subscription is null)
        {
            return SubscriptionQueryErrors.NoneYet;
        }

        var plan = await readModel.Plans.SingleAsync(item => item.Id == subscription.PlanId, cancellationToken).ConfigureAwait(false);

        return new MySubscription(
            subscription.Id,
            subscription.Status,
            plan.ToReference(query.Language),
            subscription.CurrentPeriodEnd,
            subscription.CancelAtPeriodEnd,
            subscription.CanceledAt,
            subscription.CreatedAt);
    }
}
