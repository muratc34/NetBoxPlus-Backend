namespace AuthAPI.Model.Contracts
{
    public class CreditCard
    {
        public Guid CreditCardId { get; set; }
        public string? CreditCardBrand { get; set; }
        public string? CreditCardName { get; set; }
        public string? CreditCardNumber { get; set; }
    }
}
