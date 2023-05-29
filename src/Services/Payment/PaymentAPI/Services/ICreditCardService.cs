using PaymentAPI.Model;
using Shared.Results;
using IResult = Shared.Results.IResult;

namespace PaymentAPI.Services
{
    public interface ICreditCardService
    {
        Task<IDataResult<List<CreditCard>>> GetAllAsync();
        Task<IDataResult<CreditCard>> GetByIdAsync(Guid id);
        Task<IDataResult<CreditCard>> GetByUserIdAsync(Guid userId);
        Task<IResult> AddAsync(CreditCard creditCard);
        Task<IResult> UpdateAsync(CreditCard creditCard);
        Task<IResult> DeleteAsync(CreditCard creditCard);
    }
}
