using Shared;
using SubscriptionAPI.Model;
using SubscriptionAPI.Model.Dto;
using System.Linq.Expressions;

namespace SubscriptionAPI.Infrastructure.Repositories
{
    public interface ISubscriptionRepository : IRepository<Subscription>
    {
        Task<List<SubscriptionDto>> GetSubscriptionDetailsAsync();
        Task<List<SubscriptionDto>> GetSubscriptionOneDetailAsync(Guid id);
    }
}
