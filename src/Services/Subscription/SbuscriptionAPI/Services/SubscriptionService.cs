using MassTransit;
using Shared.Contracts;
using Shared.Results;
using SubscriptionAPI.Infrastructure.Repositories;
using SubscriptionAPI.Model;
using IResult = Shared.Results.IResult;

namespace SubscriptionAPI.Services
{
    public class SubscriptionService : ISubscriptionService
    {
        private readonly ISubscriptionRepository _subscriptionRepository;
        private readonly IPublishEndpoint _publishEndpoint;

        public SubscriptionService(ISubscriptionRepository subscriptionRepository, IPublishEndpoint publishEndpoint)
        {
            _subscriptionRepository = subscriptionRepository;
            _publishEndpoint = publishEndpoint;
        }

        public async Task<IResult> AddAsync(Subscription subscription)
        {
            await _subscriptionRepository.CreateAsync(subscription);

            await _publishEndpoint.Publish(new SubscriptionCreated
            {
                SubscriptionId = subscription.Id,
                UserId = subscription.UserId,
                PlanId = subscription.PlanId,
                SubscriptionStartDate = subscription.SubscriptionStartDate,
                SubscriptionExpiration = subscription.SubscriptionExpiration,
                SubscriptionStatus = subscription.SubscriptionStatus
            });

            return new SuccessResult();
        }

        public async Task<IResult> DeleteAsync(Subscription subscription)
        {
            await _subscriptionRepository.RemoveAsync(subscription);
            return new SuccessResult();
        }

        public async Task<IDataResult<List<Subscription>>> GetAllAsync()
        {
            return new SuccessDataResult<List<Subscription>>(await _subscriptionRepository.GetAllIncludeAsync());
        }

        public async Task<IDataResult<Subscription>> GetByIdAsync(Guid id)
        {
            return new SuccessDataResult<Subscription>(await _subscriptionRepository.GetIncludeData(s => s.Id == id));
        }

        public async Task<IResult> UpdateAsync(Subscription subscription)
        {
            await _subscriptionRepository.UpdateAsync(subscription);
            return new SuccessResult();
        }
    }
}
