using AuthAPI.Model;
using AuthAPI.Model.Dto;
using AuthAPI.Security.Jwt;
using Shared.Results;
using IResult = Shared.Results.IResult;

namespace AuthAPI.Services
{
    public interface IAuthService
    {
        Task<IDataResult<User>> Register(UserForRegisterDto userForRegisterDto, string password);
        Task<IDataResult<User>> Login(UserForLoginDto userForLoginDto);
        Task<IResult> UserExists(string email);
        Task<IDataResult<AccessToken>> CreateAccessToken(User user);
        Task<IDataResult<User>> ChangePassword(UserForChangePasswordDto userForChangePasswordDto);
    }
}
