using Microsoft.EntityFrameworkCore;
using Shared.Repository;
using SubscriptionAPI.Model;
using System.Linq.Expressions;

namespace SubscriptionAPI.Infrastructure.Repositories
{
    public class SubscriptionsRepository : RepositoryBase<Subscription, SubscriptionContext>, ISubscriptionRepository
    {
        public async Task<Subscription> GetIncludeData(Expression<Func<Subscription, bool>> filter)
        {
            using (SubscriptionContext context = new SubscriptionContext())
            {
                return (await context.Set<Subscription>().Include("Plan").SingleOrDefaultAsync(filter))!;
            }
        }

        public async Task<List<Subscription>> GetAllIncludeAsync(Expression<Func<Subscription, bool>> filter = null!)
        {
            using (SubscriptionContext context = new SubscriptionContext())
            {
                return await (filter == null ? context.Set<Subscription>().Include("Plan").ToListAsync() : context.Set<Subscription>().Include("Plan").Where(filter).ToListAsync());
            }
        }

    }
}
