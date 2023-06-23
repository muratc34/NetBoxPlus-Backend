using MassTransit;
using MassTransit.Transports;
using Shared.Contracts;
using SubscriptionAPI.Infrastructure.Repositories;

namespace SubscriptionAPI.Services
{
    public class SubscriptionThreadService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        public SubscriptionThreadService(IServiceScopeFactory serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;
        }

        public async void CheckSubscriptionExpiration()
        {
            while (true)
            {
                await CheckSubs();

                await Task.Delay(TimeSpan.FromHours(1));
            }
        }

        private async Task CheckSubs()
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _subscriptionService = scope.ServiceProvider.GetRequiredService<ISubscriptionService>();
                var _publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

                var subsList = (await _subscriptionService.GetAllForThreadAsync()).Data;
                Guid userId;

                foreach (var subs in subsList)
                {
                    if (DateTime.Now > subs.SubscriptionExpiration)
                    {
                        userId = subs.UserId;
                        await _subscriptionService.DeleteAsync(subs);
                        await _publishEndpoint.Publish(new UserSubscriptionRemoved
                        {
                            UserId = userId
                        });
                    }
                }
            }
        }
    }
}
