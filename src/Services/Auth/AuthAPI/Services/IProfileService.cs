using AuthAPI.Model;
using Shared.Results;
using IResult = Shared.Results.IResult;

namespace AuthAPI.Services
{
    public interface IProfileService
    {
        Task<IDataResult<List<Profile>>> GetAllAsync();
        Task<IDataResult<Profile>> GetByIdAsync(Guid id);
        Task<IResult> AddAsync(Profile profile);
        Task<IResult> UpdateAsync(Profile profile);
        Task<IResult> DeleteAsync(Profile profile);
    }
}
