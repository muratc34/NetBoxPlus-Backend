using Shared.Results;
using SubscriptionAPI.Model;
using IResult = Shared.Results.IResult;

namespace SubscriptionAPI.Services
{
    public interface IPlanService
    {
        Task<IDataResult<List<Plan>>> GetAllAsync();
        Task<IDataResult<Plan>> GetByIdAsync(Guid id);
        Task<IResult> AddAsync(Plan plan);
        Task<IResult> UpdateAsync(Plan plan);
        Task<IResult> DeleteAsync(Plan plan);
    }
}
