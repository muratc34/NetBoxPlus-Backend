using MovieAPI.Infrastructure.Repositories;
using MovieAPI.Model;
using Shared.Results;
using IResult = Shared.Results.IResult;

namespace MovieAPI.Services
{
    public class AgeRatingService : IAgeRatingService
    {
        private readonly IAgeRatingRepository _ageRatingRepository;

        public AgeRatingService(IAgeRatingRepository ageRatingRepository)
        {
            _ageRatingRepository = ageRatingRepository;
        }

        public async Task<IResult> AddAsync(AgeRating ageRating)
        {
            ageRating.Id = Guid.NewGuid();

            await _ageRatingRepository.CreateAsync(ageRating);
            return new SuccessResult();
        }

        public async Task<IResult> DeleteAsync(AgeRating ageRating)
        {
            await _ageRatingRepository.RemoveAsync(ageRating);
            return new SuccessResult();
        }

        public async Task<IDataResult<List<AgeRating>>> GetAllAsync()
        {
            return new SuccessDataResult<List<AgeRating>>(await _ageRatingRepository.GetAllAsync());
        }

        public async Task<IDataResult<AgeRating>> GetByIdAsync(Guid id)
        {
            return new SuccessDataResult<AgeRating>(await _ageRatingRepository.GetAsync(b => b.Id == id));
        }

        public async Task<IResult> UpdateAsync(AgeRating ageRating)
        {
            await _ageRatingRepository.UpdateAsync(ageRating);
            return new SuccessResult();
        }
    }
}
