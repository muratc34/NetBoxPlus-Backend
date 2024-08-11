using Auth.Application.Abstractions;
using Auth.Domain.Authentication;
using Auth.Domain.Users;
using Domain.Core.Errors;
using Shared.Application.Abstractions;
using Shared.Domain.Result;

namespace Auth.Application.Authentication.Login;

public sealed class LoginCommandHandler : ICommandHandler<LoginCommand, Result<AccessToken>>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtProvider _jwtProvider;

    public LoginCommandHandler(IUserRepository userRepository, IPasswordHasher passwordHasher, IJwtProvider jwtProvider)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _jwtProvider = jwtProvider;
    }

    public async Task<Result<AccessToken>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        Result<Email> emailResult = Email.Create(request.Email);

        if (!emailResult.IsSuccess)
        {
            return Result.Failure<AccessToken>(DomainErrors.Authentication.InvalidEmailOrPassword);
        }
        var user = await _userRepository.GetAsync(x => x.Email.Value == emailResult.Data!.Value);

        if (user is null)
        {
            return Result.Failure<AccessToken>(DomainErrors.Authentication.InvalidEmailOrPassword);
        }

        bool passwordValid = _passwordHasher.VerifyPasswordHash(request.Password, user.PasswordHash, user.PasswordSalt);

        if (!passwordValid)
        {
            return Result.Failure<AccessToken>(DomainErrors.Authentication.InvalidEmailOrPassword);
        }
        var token = _jwtProvider.CreateToken(user);

        return Result.Success(token);
    }
}
