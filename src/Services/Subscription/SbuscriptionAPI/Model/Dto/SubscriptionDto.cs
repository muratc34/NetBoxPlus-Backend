namespace SubscriptionAPI.Model.Dto
{
    public class SubscriptionDto
    {
        public Guid SubscriptionId { get; set; }
        public Guid UserId { get; set; }
        public DateTimeOffset SubscriptionStartDate { get; set; }
        public DateTimeOffset SubscriptionExpiration { get; set; }
        public bool SubscriptionStatus { get; set; }
        public Plan? Plan { get; set; }

    }
}
