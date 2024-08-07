﻿using AuthAPI.Infrastructure.Repositories;
using AuthAPI.Model;
using Shared.Results;
using IResult = Shared.Results.IResult;

namespace AuthAPI.Services
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;

        public UserService(IUserRepository userRepository)
        {
            _userRepository = userRepository;
        }

        public async Task<IResult> AddAsync(User user)
        {
            await _userRepository.CreateAsync(user);
            return new SuccessResult("Kullanıcı eklendi.");
        }

        public async Task<IResult> DeleteAsync(User user)
        {
            await _userRepository.RemoveAsync(user);
            return new SuccessResult("Kullanıcı silindi.");
        }

        public async Task<IDataResult<List<User>>> GetAllAsync()
        {
            return new SuccessDataResult<List<User>>(await _userRepository.GetAllIncludeAsync());
        }

        public async Task<User> GetByEmailAsync(string email)
        {
            return  await _userRepository.GetIncludeData(u => u.Email == email);
        }

        public async Task<IDataResult<User>> GetByIdAsync(Guid id)
        {
            return new SuccessDataResult<User>(await _userRepository.GetIncludeData(u => u.Id == id));
        }

        public async Task<List<OperationClaim>> GetClaimsAsync(User user)
        {
            return await _userRepository.GetClaims(user);
        }

        public async Task<IResult> UpdateAsync(User user)
        {
            await _userRepository.UpdateAsync(user);
            return new SuccessResult("Kullanıcı güncellendi.");
        }

        public async Task<User> GetByIdForEventAsync(Guid id)
        {
            return await _userRepository.GetIncludeData(u => u.Id == id);
        }
    }
}
