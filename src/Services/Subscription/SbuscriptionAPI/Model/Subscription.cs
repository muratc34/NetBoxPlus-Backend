using Shared;

namespace SubscriptionAPI.Model
{
    public class Subscription : IEntity
    {
        public Guid Id { get; set; }
        public Guid PlanId { get; set; }
        public Guid UserId { get; set; }
        public DateTimeOffset SubscriptionStartDate { get; set; }
        public DateTimeOffset SubscriptionExpiration { get; set; }
        public bool SubscriptionStatus { get; set; }
        public Plan? Plan { get; set; }

    }
}
