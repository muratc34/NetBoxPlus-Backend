using Shared;

namespace PaymentAPI.Model
{
    public class CreditCard:IEntity
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string? CardBrand { get; set; }
        public string? CardName { get; set; }
        public string? CardNumber { get; set; }
        public string? ExpireYear { get; set; }
        public string? ExpireMonth { get; set; }
        public string? CVV { get; set; }
    }
}
