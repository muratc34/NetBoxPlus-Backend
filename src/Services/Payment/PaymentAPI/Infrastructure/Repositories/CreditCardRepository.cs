using PaymentAPI.Model;
using Shared.Repository;

namespace PaymentAPI.Infrastructure.Repositories
{
    public class CreditCardRepository : RepositoryBase<CreditCard, PaymentContext>, ICreditCardRepository
    {
    }
}
