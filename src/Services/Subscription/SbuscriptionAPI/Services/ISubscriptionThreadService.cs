namespace SubscriptionAPI.Services
{
    public interface ISubscriptionThreadService
    {
        Task CheckSubscriptionExpiration();
    }
}
