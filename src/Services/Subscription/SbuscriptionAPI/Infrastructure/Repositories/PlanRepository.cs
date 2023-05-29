using Shared.Repository;
using SubscriptionAPI.Model;

namespace SubscriptionAPI.Infrastructure.Repositories
{
    public class PlanRepository : RepositoryBase<Plan, SubscriptionContext>, IPlanRepository
    {
    }
}
