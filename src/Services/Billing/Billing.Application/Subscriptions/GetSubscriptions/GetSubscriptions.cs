using Billing.Application.Abstractions;
using Billing.Domain.Plans;
using Billing.Domain.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Application.Paging;
using Shared.Application.Validation;
using Shared.Domain;

namespace Billing.Application.Subscriptions.GetSubscriptions;

/// <summary>Subscriptions for the admin panel, newest first.</summary>
public sealed record GetSubscriptionsQuery(SubscriptionStatus? Status, Guid? UserId, int PageNumber, int PageSize) : IQuery<Page<SubscriptionSummary>>;

/// <param name="Email">Null for a user billing never heard of, which should not happen.</param>
public sealed record SubscriptionSummary(
    Guid Id,
    Guid UserId,
    string? Email,
    Guid PlanId,
    string PlanName,
    SubscriptionStatus Status,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    DateTimeOffset CreatedAt);

internal sealed class GetSubscriptionsValidator : IValidator<GetSubscriptionsQuery>
{
    public IEnumerable<ValidationFailure> Validate(GetSubscriptionsQuery instance)
    {
        if (!PageLimits.IsValid(instance.PageNumber, instance.PageSize))
        {
            yield return new ValidationFailure("Page", PageLimits.OutOfRange.Code, PageLimits.OutOfRange.Message);
        }
    }
}

internal sealed class GetSubscriptionsHandler(IBillingReadModel readModel) : IQueryHandler<GetSubscriptionsQuery, Page<SubscriptionSummary>>
{
    public async Task<Result<Page<SubscriptionSummary>>> Handle(GetSubscriptionsQuery query, CancellationToken cancellationToken)
    {
        var subscriptions = readModel.Subscriptions;
        if (query.Status is { } status)
        {
            subscriptions = subscriptions.Where(subscription => subscription.Status == status);
        }

        if (query.UserId is { } userId)
        {
            subscriptions = subscriptions.Where(subscription => subscription.UserId == userId);
        }

        var totalCount = await subscriptions.CountAsync(cancellationToken).ConfigureAwait(false);
        var page = await subscriptions
            .OrderByDescending(subscription => subscription.CreatedAt)
            .ThenByDescending(subscription => subscription.Id)
            .Skip(PageLimits.Offset(query.PageNumber, query.PageSize))
            .Take(query.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var userIds = page.Select(subscription => subscription.UserId).Distinct().ToList();
        var emails = await readModel.Customers
            .Where(customer => userIds.Contains(customer.UserId))
            .ToDictionaryAsync(customer => customer.UserId, customer => customer.Email, cancellationToken)
            .ConfigureAwait(false);
        var planIds = page.Select(subscription => subscription.PlanId).Distinct().ToList();
        var plans = await readModel.Plans
            .Where(plan => planIds.Contains(plan.Id))
            .ToDictionaryAsync(plan => plan.Id, cancellationToken)
            .ConfigureAwait(false);

        var items = page
            .Select(subscription => new SubscriptionSummary(
                subscription.Id,
                subscription.UserId,
                emails.GetValueOrDefault(subscription.UserId),
                subscription.PlanId,
                plans[subscription.PlanId].TextFor(Languages.Default).Name,
                subscription.Status,
                subscription.CurrentPeriodEnd,
                subscription.CancelAtPeriodEnd,
                subscription.CreatedAt))
            .ToList();

        return new Page<SubscriptionSummary>(items, query.PageNumber, query.PageSize, totalCount);
    }
}
