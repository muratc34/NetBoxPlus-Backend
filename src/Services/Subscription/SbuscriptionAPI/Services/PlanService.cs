using Shared.Results;
using SubscriptionAPI.Infrastructure.Repositories;
using SubscriptionAPI.Model;
using IResult = Shared.Results.IResult;

namespace SubscriptionAPI.Services
{
    public class PlanService : IPlanService
    {
        private readonly IPlanRepository _planRepository;

        public PlanService(IPlanRepository planRepository)
        {
            _planRepository = planRepository;
        }

        public async Task<IResult> AddAsync(Plan plan)
        {
            await _planRepository.CreateAsync(plan);
            return new SuccessResult();
        }

        public async Task<IResult> DeleteAsync(Plan plan)
        {
            await _planRepository.RemoveAsync(plan);
            return new SuccessResult();
        }

        public async Task<IDataResult<List<Plan>>> GetAllAsync()
        {
            return new SuccessDataResult<List<Plan>>(await _planRepository.GetAllAsync());
        }

        public async Task<IDataResult<Plan>> GetByIdAsync(Guid id)
        {
            return new SuccessDataResult<Plan>(await _planRepository.GetAsync(p => p.Id == id));
        }

        public async Task<IResult> UpdateAsync(Plan plan)
        {
            await _planRepository.UpdateAsync(plan);
            return new SuccessResult();
        }
    }
}
