using Auth.Application.Abstractions;
using Auth.Domain.Users;
using Domain.Core.Errors;
using Shared.Application.Abstractions;
using Shared.Domain.Result;
using Shared.UnitOfWork;

namespace Auth.Application.Authentication.ChangePassword;

public sealed class ChangePasswordCommandHandler : ICommandHandler<ChangePasswordCommand, Result>
{
    private readonly IUserRepository _userRepository;
    private readonly IUserContext _userContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;

    public ChangePasswordCommandHandler(
        IUserRepository userRepository, 
        IUnitOfWork unitOfWork, 
        IPasswordHasher passwordHasher, 
        IUserContext userContext)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _userContext = userContext;
    }

    public async Task<Result> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        if (request.UserId != _userContext.UserId)
        {
            return Result.Failure(DomainErrors.User.InvalidPermissions);
        }

        Result<Password> passwordResult = Password.Create(request.Password);
        if (!passwordResult.IsSuccess)
        {
            return Result.Failure(passwordResult.Error!);
        }

        var user = await _userRepository.GetAsync(user => user.Id == request.UserId);
        if (user is null)
        {
            return Result.Failure(DomainErrors.User.NotFound);
        }

        byte[] passwordHash;
        _passwordHasher.ChangePassword(passwordResult.Data!.Value, out passwordHash,  user.PasswordSalt);
        Result result = user.ChangePassword(passwordResult.Data!.Value, passwordHash);

        if (!result.IsSuccess)
        {
            return Result.Failure(result.Error!);
        }

        await _userRepository.UpdateAsync(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
