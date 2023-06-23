using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Shared.Contracts
{
    public class SubscriptionCreated
    {
        public Guid SubscriptionId { get; set; }
        public Guid PlanId { get; set; }
        public Guid UserId { get; set; }
        public DateTimeOffset SubscriptionStartDate { get; set; }
        public DateTimeOffset SubscriptionExpiration { get; set; }
        public bool SubscriptionStatus { get; set; }
    }
}
