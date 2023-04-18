using MovieAPI.Model;
using Shared.Results;
using IResult = Shared.Results.IResult;

namespace MovieAPI.Services
{
    public interface IAgeRatingService
    {
        Task<IDataResult<List<AgeRating>>> GetAllAsync();
        Task<IDataResult<AgeRating>> GetByIdAsync(Guid id);
        Task<IResult> AddAsync(AgeRating ageRating);
        Task<IResult> UpdateAsync(AgeRating ageRating);
        Task<IResult> DeleteAsync(AgeRating ageRating);
    }
}
