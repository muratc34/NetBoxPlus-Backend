using AuthAPI.Model;
using Shared.Results;
using IResult = Shared.Results.IResult;

namespace AuthAPI.Services
{
    public interface IUserService
    {
        Task<List<OperationClaim>> GetClaimsAsync(User user);
        Task<User> GetByEmailAsync(string email);
        Task<IDataResult<List<User>>> GetAllAsync();
        Task<IDataResult<User>> GetByIdAsync(Guid id);
        Task<IResult> AddAsync(User user);
        Task<IResult> UpdateAsync(User user);
        Task<IResult> DeleteAsync(User user);
    }
}
