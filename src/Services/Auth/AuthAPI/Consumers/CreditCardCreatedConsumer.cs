using AuthAPI.Infrastructure;
using AuthAPI.Model;
using AuthAPI.Model.Contracts;
using AuthAPI.Services;
using MassTransit;
using Shared.Contracts;

namespace AuthAPI.Consumers
{
    public class CreditCardCreatedConsumer : IConsumer<CreditCardCreated>
    {
        private readonly AuthContext _authContext;
        private readonly IUserService _userService;

        public CreditCardCreatedConsumer(AuthContext authContext, IUserService userService)
        {
            _authContext = authContext;
            _userService = userService;
        }

        public async Task Consume(ConsumeContext<CreditCardCreated> context)
        {
            var newCreditCard = new CreditCard
            {
                CreditCardId = context.Message.CreditCardId,
                CreditCardBrand = context.Message.CreditCardBrand,
                CreditCardName = context.Message.CreditCardName,
                CreditCardNumber = context.Message.CreditCardNumber
            };

            User user = await _userService.GetByIdForEventAsync(context.Message.UserId!);

            user.PaymentId = context.Message.CreditCardId;

            _authContext.CreditCards.Add(newCreditCard);

            await _authContext.SaveChangesAsync();

            await _userService.UpdateAsync(user);
        }
    }
}
