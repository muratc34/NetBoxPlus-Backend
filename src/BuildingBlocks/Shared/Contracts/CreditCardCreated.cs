using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared.Contracts
{
    public class CreditCardCreated
    {
        public Guid CreditCardId { get; set; }
        public Guid UserId { get; set; }
        public string? CreditCardBrand { get; set; }
        public string? CreditCardName { get; set; }
        public string? CreditCardNumber { get; set; }
    }
}
