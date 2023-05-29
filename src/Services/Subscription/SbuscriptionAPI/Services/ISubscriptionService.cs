using Shared.Results;
using SubscriptionAPI.Model;
using IResult = Shared.Results.IResult;

namespace SubscriptionAPI.Services
{
    public interface ISubscriptionService
    {
        Task<IDataResult<List<Subscription>>> GetAllAsync();
        Task<IDataResult<Subscription>> GetByIdAsync(Guid id);
        Task<IResult> AddAsync(Subscription subscription);
        Task<IResult> UpdateAsync(Subscription subscription);
        Task<IResult> DeleteAsync(Subscription subscription);
    }
}
