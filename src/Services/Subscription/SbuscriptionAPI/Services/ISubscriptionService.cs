using Shared.Results;
using SubscriptionAPI.Model;
using SubscriptionAPI.Model.Dto;
using IResult = Shared.Results.IResult;

namespace SubscriptionAPI.Services
{
    public interface ISubscriptionService
    {
        Task<IDataResult<List<Subscription>>> GetAllForThreadAsync();
        Task<IDataResult<List<SubscriptionDto>>> GetAllAsync();
        Task<IDataResult<List<SubscriptionDto>>> GetByIdAsync(Guid id);
        Task<IResult> AddAsync(Subscription subscription);
        Task<IResult> UpdateAsync(Subscription subscription);
        Task<IResult> DeleteAsync(Subscription subscription);
    }
}
