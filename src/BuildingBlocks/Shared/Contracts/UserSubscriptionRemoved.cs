using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared.Contracts
{
    public class UserSubscriptionRemoved
    {
        public Guid UserId { get; set; }
    }
}
