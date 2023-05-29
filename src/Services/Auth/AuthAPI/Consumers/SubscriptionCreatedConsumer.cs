using AuthAPI.Infrastructure;
using AuthAPI.Model;
using AuthAPI.Services;
using MassTransit;
using Shared.Contracts;

namespace AuthAPI.Consumers
{
    public class SubscriptionCreatedConsumer : IConsumer<SubscriptionCreated>
    {
        private readonly IUserService _userService;

        public SubscriptionCreatedConsumer(IUserService userService)
        {
            _userService = userService;
        }

        public async Task Consume(ConsumeContext<SubscriptionCreated> context)
        {
            User user = await _userService.GetByIdForEventAsync(context.Message.UserId!);

            user.SubscriptionId = context.Message.SubscriptionId;

            await _userService.UpdateAsync(user);
        }
    }
}
