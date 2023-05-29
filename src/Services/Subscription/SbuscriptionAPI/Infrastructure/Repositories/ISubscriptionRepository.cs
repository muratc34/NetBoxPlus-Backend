using Shared;
using SubscriptionAPI.Model;
using System.Linq.Expressions;

namespace SubscriptionAPI.Infrastructure.Repositories
{
    public interface ISubscriptionRepository : IRepository<Subscription>
    {
        Task<List<Subscription>> GetAllIncludeAsync(Expression<Func<Subscription, bool>> filter = null!);
        Task<Subscription> GetIncludeData(Expression<Func<Subscription, bool>> filter);
    }
}
