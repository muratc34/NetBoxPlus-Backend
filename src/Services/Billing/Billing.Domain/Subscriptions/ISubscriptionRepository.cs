namespace Billing.Domain.Subscriptions;

public interface ISubscriptionRepository
{
    /// <summary>Whether the user has a subscription that is not canceled.</summary>
    Task<bool> HasLiveAsync(Guid userId, CancellationToken cancellationToken);

    Task<Subscription?> GetByProviderIdAsync(string providerSubscriptionId, CancellationToken cancellationToken);

    void Add(Subscription subscription);
}
