using Auth.Application.Abstractions;
using Auth.Domain.Authentication;
using Auth.Domain.Users;
using Domain.Core.Errors;
using Shared.Application.Abstractions;
using Shared.Domain.Result;
using Shared.UnitOfWork;

namespace Auth.Application.Authentication.Register;

public sealed class RegisterCommandHandler : ICommandHandler<RegisterCommand, Result<AccessToken>>
{
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtProvider _jwtProvider;

    public RegisterCommandHandler(
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IJwtProvider jwtProvider)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _jwtProvider = jwtProvider;
    }

    public async Task<Result<AccessToken>> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        Result<FirstName> firstNameResult = FirstName.Create(request.FirstName);
        Result<LastName> lastNameResult = LastName.Create(request.LastName);
        Result<Email> emailResult = Email.Create(request.Email);
        Result<Password> passwordResult = Password.Create(request.Password);

        Result firstFailureOrSuccess = Result.FirstFailureOrSuccess(firstNameResult, lastNameResult, emailResult, passwordResult);

        if (!firstFailureOrSuccess.IsSuccess)
        {
            return Result.Failure<AccessToken>(firstFailureOrSuccess.Error!);
        }

        if (await GetByEmailAsync(request.Email) is not null)
        {
            return Result.Failure<AccessToken>(DomainErrors.User.DuplicateEmail);
        }

        byte[] passwordHash, passwordSalt;
        _passwordHasher.CreatePasswordHash(passwordResult.Data!.Value, out passwordHash, out passwordSalt);

        var user = User.Create(firstNameResult.Data!, lastNameResult.Data!, emailResult.Data!, passwordHash, passwordSalt);
        await _userRepository.CreateAsync(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var token = _jwtProvider.CreateToken(user);
        return Result.Success(token);
    }

    private async Task<User> GetByEmailAsync(string email)
    {
        return await _userRepository.GetAsync(u => u.Email.Value.Equals(email));
    }
}
