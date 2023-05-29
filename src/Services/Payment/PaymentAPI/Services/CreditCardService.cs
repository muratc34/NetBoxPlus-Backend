using MassTransit;
using Microsoft.AspNetCore.Identity;
using PaymentAPI.Infrastructure.Repositories;
using PaymentAPI.Model;
using PaymentAPI.Security.Encryption;
using Shared.Contracts;
using Shared.Results;
using IResult = Shared.Results.IResult;

namespace PaymentAPI.Services
{
    public class CreditCardService : ICreditCardService
    {
        private readonly ICreditCardRepository _creditCardRepository;
        private readonly IPublishEndpoint _publishEndpoint;

        public CreditCardService(ICreditCardRepository creditCardRepository, IPublishEndpoint publishEndpoint)
        {
            _creditCardRepository = creditCardRepository;
            _publishEndpoint = publishEndpoint;
        }

        public async Task<IResult> AddAsync(CreditCard creditCard)
        {
            if (creditCard != null )
            {
                var cardNumber = EncryptionService.Encrypt(creditCard.CardNumber!);
                var expireYear = EncryptionService.Encrypt(creditCard.ExpireYear!);
                var expireMonth = EncryptionService.Encrypt(creditCard.ExpireMonth!);
                var cvv = EncryptionService.Encrypt(creditCard.CVV!);

                var encryptedCreditCard = new CreditCard
                {
                    Id = Guid.NewGuid(),
                    UserId = creditCard.UserId,
                    CardBrand = creditCard.CardBrand,
                    CardName = creditCard.CardName,
                    CardNumber = cardNumber,
                    ExpireYear = expireYear,
                    ExpireMonth = expireMonth,
                    CVV = cvv
                };

                await _creditCardRepository.CreateAsync(encryptedCreditCard);

                var censoredCardNumber = MarkCardNumber(encryptedCreditCard.CardNumber!);

                await _publishEndpoint.Publish<CreditCardCreated>(new
                {
                    CreditCardId = encryptedCreditCard.Id,
                    UserId = encryptedCreditCard.UserId,
                    CreditCardBrand = encryptedCreditCard.CardBrand,
                    CreditCardName = encryptedCreditCard.CardName,
                    CreditCardNumber = censoredCardNumber
                });

                return new SuccessResult();
            }
            return new ErrorResult();
        }

        public async Task<IResult> DeleteAsync(CreditCard creditCard)
        {
            await _creditCardRepository.RemoveAsync(creditCard);
            return new SuccessResult();
        }

        public async Task<IDataResult<List<CreditCard>>> GetAllAsync()
        {
            return new SuccessDataResult<List<CreditCard>>(await _creditCardRepository.GetAllAsync());
        }

        public async Task<IDataResult<CreditCard>> GetByIdAsync(Guid id)
        {
            var creditCard = await _creditCardRepository.GetAsync(cc => cc.Id == id);

            var censoredCardNumber = MarkCardNumber(creditCard.CardNumber!);

            var decryptedCreditCard = new CreditCard
            {
                Id = creditCard.Id,
                UserId = creditCard.UserId,
                CardBrand = creditCard.CardBrand,
                CardName = creditCard.CardName,
                CardNumber = censoredCardNumber,
                ExpireYear = creditCard.ExpireYear,
                ExpireMonth = creditCard.ExpireMonth,
                CVV = creditCard.CVV
            };

            return new SuccessDataResult<CreditCard>(decryptedCreditCard);
        }

        public async Task<IDataResult<CreditCard>> GetByUserIdAsync(Guid userId)
        {
            return new SuccessDataResult<CreditCard>(await _creditCardRepository.GetAsync(cc => cc.UserId == userId));
        }

        public async Task<IResult> UpdateAsync(CreditCard creditCard)
        {
            await _creditCardRepository.UpdateAsync(creditCard);
            return new SuccessResult();
        }

        private string MarkCardNumber(string cardNumber)
        {
            var decryptedCardNumber = EncryptionService.Decrypt(cardNumber);
            var lastDigitsCardNumber = decryptedCardNumber.Substring(12);
            var censoredCardNumber = string.Concat(new String('*', decryptedCardNumber.Length - lastDigitsCardNumber.Length), lastDigitsCardNumber);

            return censoredCardNumber;
        }
    }
}
