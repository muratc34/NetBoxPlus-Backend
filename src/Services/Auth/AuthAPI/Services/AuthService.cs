﻿using AuthAPI.Model;
using AuthAPI.Model.Dto;
using AuthAPI.Security.Hashing;
using AuthAPI.Security.Jwt;
using Shared.Results;
using static MassTransit.Monitoring.Performance.BuiltInCounters;
using IResult = Shared.Results.IResult;

namespace AuthAPI.Services
{
    public class AuthService : IAuthService
    {
        private IUserService _userService;
        private ITokenHelper _tokenHelper;

        public AuthService(IUserService userService, ITokenHelper tokenHelper)
        {
            _userService = userService;
            _tokenHelper = tokenHelper;
        }

        public async Task<IDataResult<AccessToken>> CreateAccessToken(User user)
        {
            var claims = await _userService.GetClaimsAsync(user);
            var accessToken = _tokenHelper.CreateToken(user, claims);
            return new SuccessDataResult<AccessToken>(accessToken, "Token Oluşturuldu");
        }

        public async Task<IDataResult<User>> Login(UserForLoginDto userForLoginDto)
        {
            var userToCheck = await _userService.GetByEmailAsync(userForLoginDto.Email!);
            if (userToCheck == null)
                return new ErrorDataResult<User>("Kullanıcı kayıtlı değil!");
            if (!HashingHelper.VerifyPasswordHash(userForLoginDto.Password!, userToCheck.PasswordHash!, userToCheck.PasswordSalt!))
            {
                return new ErrorDataResult<User>("Şifre yanlış!");
            }
            return new SuccessDataResult<User>(userToCheck, "Başarıyla giriş yapıldı!");
        }

        public async Task<IDataResult<User>> Register(UserForRegisterDto userForRegisterDto, string password)
        {
            byte[] passwordHash, passwordSalt;
            HashingHelper.CreatePasswordHash(password, out passwordHash, out passwordSalt);
            var user = new User
            {
                Email = userForRegisterDto.Email,
                FirstName = userForRegisterDto.FirstName,
                LastName = userForRegisterDto.LastName,
                PasswordHash = passwordHash,
                PasswordSalt = passwordSalt,
                Status = true
            };
            await _userService.AddAsync(user);
            return new SuccessDataResult<User>(user, "Başarıyla kayıt olundu!");
        }

        public async Task<IDataResult<User>> ChangePassword(UserForChangePasswordDto userForChangePasswordDto)
        {
            byte[] passwordHash, passwordSalt;
            HashingHelper.CreatePasswordHash(userForChangePasswordDto.NewPassword!, out passwordHash, out passwordSalt);
            var data = await _userService.GetByIdAsync(userForChangePasswordDto.Id);

            if (!HashingHelper.VerifyPasswordHash(userForChangePasswordDto.CurrentPassword!, data.Data.PasswordHash!, data.Data.PasswordSalt!))
            {
                return new ErrorDataResult<User>("Şifre yanlış!");
            }

            var user = new User
            {
                Id = userForChangePasswordDto.Id,
                Email = data.Data.Email,
                FirstName = data.Data.FirstName,
                LastName = data.Data.LastName,
                OperationClaims = data.Data.OperationClaims,
                PasswordHash = passwordHash,
                PasswordSalt = passwordSalt,
                Status = data.Data.Status
            };
            
            await _userService.UpdateAsync(user);

            return new SuccessDataResult<User>("Şifre başarıyla değiştirildi.");
        }

        public async Task<IResult> UserExists(string email)
        {
            if (await _userService.GetByEmailAsync(email) != null)
            {
                return new ErrorResult("Kullanıcı zaten kayıtlı!");
            }
            return new SuccessResult();
        }
    }
}
