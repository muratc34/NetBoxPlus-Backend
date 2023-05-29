using AuthAPI.Infrastructure.Repositories;
using AuthAPI.Model;
using Shared.Results;
using IResult = Shared.Results.IResult;

namespace AuthAPI.Services
{
    public class ProfileService : IProfileService
    {
        private readonly IProfileRepository _profileRepository;

        public ProfileService(IProfileRepository profileRepository)
        {
            _profileRepository = profileRepository;
        }

        public async Task<IResult> AddAsync(Profile profile)
        {
            await _profileRepository.CreateAsync(profile);
            return new SuccessResult();
        }

        public async Task<IResult> DeleteAsync(Profile profile)
        {
            await _profileRepository.RemoveAsync(profile);
            return new SuccessResult();
        }

        public async Task<IDataResult<List<Profile>>> GetAllAsync()
        {
            return new SuccessDataResult<List<Profile>>(await _profileRepository.GetAllAsync());
        }

        public async Task<IDataResult<Profile>> GetByIdAsync(Guid id)
        {
            return new SuccessDataResult<Profile>(await _profileRepository.GetAsync(p => p.Id == id));
        }

        public async Task<IResult> UpdateAsync(Profile profile)
        {
            await _profileRepository.UpdateAsync(profile);
            return new SuccessResult();
        }
    }
}
