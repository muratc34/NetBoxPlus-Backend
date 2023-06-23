using AuthAPI.Model;
using AuthAPI.Services;
using MassTransit;
using Shared.Contracts;

namespace AuthAPI.Consumers
{
    public class UserSubscriptionRemovedConsumer : IConsumer<UserSubscriptionRemoved>
    {
        private readonly IUserService _userService;

        public UserSubscriptionRemovedConsumer(IUserService userService)
        {
            _userService = userService;
        }

        public async Task Consume(ConsumeContext<UserSubscriptionRemoved> context)
        {
            var user = (await _userService.GetByIdAsync(context.Message.UserId)).Data;
            user.SubscriptionId = null;
            await _userService.UpdateAsync(user);
        }
    }
}
