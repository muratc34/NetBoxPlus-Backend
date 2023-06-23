using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Repository;
using SubscriptionAPI.Model;
using SubscriptionAPI.Model.Dto;
using System.Linq.Expressions;

namespace SubscriptionAPI.Infrastructure.Repositories
{
    public class SubscriptionRepository : RepositoryBase<Subscription, SubscriptionContext>, ISubscriptionRepository
    {
        
        public async Task<List<SubscriptionDto>> GetSubscriptionDetailsAsync()
        {
            using (SubscriptionContext context = new SubscriptionContext())
            {
                var result = from subs in context.Subscriptions
                             join plan in context.Plans
                             on subs.PlanId equals plan.Id
                             select new SubscriptionDto
                             {
                                 SubscriptionId = subs.Id, SubscriptionStartDate = subs.SubscriptionStartDate,
                                 SubscriptionExpiration = subs.SubscriptionExpiration, SubscriptionStatus = subs.SubscriptionStatus,
                                 UserId = subs.UserId,
                                 Plan = new Plan
                                 {
                                     Id= plan.Id, Quality = plan.Quality,
                                     ProfileCount = plan.ProfileCount,
                                     Amount = plan.Amount, PlanName = plan.PlanName
                                 }
                             };

                return await result.ToListAsync();
            }
        }

        public async Task<List<SubscriptionDto>> GetSubscriptionOneDetailAsync(Guid id)
        {
            using (SubscriptionContext context = new SubscriptionContext())
            {
                var result = from subs in context.Subscriptions where subs.Id == id
                             join plan in context.Plans
                             on subs.PlanId equals plan.Id
                             select new SubscriptionDto
                             {
                                 SubscriptionId = subs.Id,
                                 SubscriptionStartDate = subs.SubscriptionStartDate,
                                 SubscriptionExpiration = subs.SubscriptionExpiration,
                                 SubscriptionStatus = subs.SubscriptionStatus,
                                 UserId = subs.UserId,
                                 Plan = new Plan
                                 {
                                     Id = plan.Id,Quality = plan.Quality,
                                     ProfileCount = plan.ProfileCount,
                                     Amount = plan.Amount, PlanName = plan.PlanName
                                 }
                             };

                return await result.ToListAsync();
            }
        }

    }
}
