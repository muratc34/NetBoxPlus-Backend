using Shared.Domain;

namespace Billing.Domain.Subscriptions;

public enum SubscriptionStatus
{
    Incomplete,
    Active,
    PastDue,
    Canceled,
}

public static class SubscriptionErrors
{
    public static readonly Error AlreadyActive = Error.Conflict("Subscription.AlreadyActive", "There is already a subscription; change its plan from the billing portal.");

    public static readonly Error None = Error.Conflict("Subscription.None", "There is nothing to manage yet: no subscription has been started.");
}

/// <summary>What the payment provider currently says about a subscription.</summary>
public sealed record SubscriptionState(SubscriptionStatus Status, Guid PlanId, DateTimeOffset? CurrentPeriodEnd, bool CancelAtPeriodEnd);

/// <summary>
/// A mirror of the subscription at the payment provider, which decides everything about it. A user
/// has at most one that is not canceled.
/// </summary>
public sealed class Subscription : AggregateRoot, IAuditableEntity
{
    public const int ProviderSubscriptionIdMaxLength = 128;

    private Subscription(Guid id, Guid userId, string providerSubscriptionId)
        : base(id)
    {
        UserId = userId;
        ProviderSubscriptionId = providerSubscriptionId;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private Subscription()
    {
    }

    public Guid UserId { get; private set; }

    public Guid PlanId { get; private set; }

    public string ProviderSubscriptionId { get; private set; } = string.Empty;

    public SubscriptionStatus Status { get; private set; }

    public DateTimeOffset? CurrentPeriodEnd { get; private set; }

    public bool CancelAtPeriodEnd { get; private set; }

    public DateTimeOffset? CanceledAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ModifiedAt { get; private set; }

    public static Subscription Start(Guid userId, string providerSubscriptionId, SubscriptionState state, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerSubscriptionId);

        var subscription = new Subscription(Guid.CreateVersion7(), userId, providerSubscriptionId);
        subscription.Apply(state, now);
        return subscription;
    }

    /// <summary>Takes over the provider's state. A canceled subscription never comes back; a late message cannot revive it.</summary>
    public void Apply(SubscriptionState state, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (Status == SubscriptionStatus.Canceled)
        {
            return;
        }

        Status = state.Status;
        PlanId = state.PlanId;
        CurrentPeriodEnd = state.CurrentPeriodEnd;
        CancelAtPeriodEnd = state.CancelAtPeriodEnd;

        if (state.Status == SubscriptionStatus.Canceled)
        {
            CanceledAt = now;
        }
    }
}
