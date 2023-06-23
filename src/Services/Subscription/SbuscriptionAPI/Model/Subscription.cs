using Shared;

namespace SubscriptionAPI.Model
{
    public class Subscription : IEntity
    {
        public Guid Id { get; set; }
        public Guid PlanId { get; set; }
        public Guid UserId { get; set; }
        public DateTime SubscriptionStartDate { get; set; }
        public DateTime SubscriptionExpiration { get; set; }
        public bool SubscriptionStatus { get; set; }
    }
}
