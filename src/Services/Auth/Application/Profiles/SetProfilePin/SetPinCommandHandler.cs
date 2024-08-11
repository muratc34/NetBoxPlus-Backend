using Auth.Application.Abstractions;
using Auth.Domain.Profiles;
using Domain.Core.Errors;
using Shared.Application.Abstractions;
using Shared.Domain.Result;
using Shared.UnitOfWork;

namespace Auth.Application.Profiles.SetProfilePin;

public sealed class SetPinCommandHandler : ICommandHandler<SetProfilePinCommand, Result>
{
    private readonly IProfileRepository _profileRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly IPasswordHasher _passwordHasher;

    public SetPinCommandHandler(IProfileRepository profileRepository, IUnitOfWork unitOfWork, IUserContext userContext, IPasswordHasher passwordHasher)
    {
        _profileRepository = profileRepository;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
        _passwordHasher = passwordHasher;
    }

    public async Task<Result> Handle(SetProfilePinCommand request, CancellationToken cancellationToken)
    {
        if (!_userContext.IsAuthenticated)
        {
            return Result.Failure(DomainErrors.User.InvalidPermissions);
        }

        Result<Pin> pinResult = Pin.Create(request.Pin);
        if (!pinResult.IsSuccess)
        {
            return Result.Failure(pinResult.Error!);
        }

        var profile = await _profileRepository.GetAsync(p => p.Id == request.ProfileId);
        if (profile is null)
        {
            return Result.Failure(DomainErrors.Profile.NotFound);
        }
        byte[] pinHash, pinSalt;
        _passwordHasher.CreatePasswordHash(pinResult.Data!.Value, out pinHash, out pinSalt);

        var result = profile.SetProfilePin(pinHash, pinSalt);

        if (!result.IsSuccess)
        {
            return Result.Failure(result.Error!);
        }

        await _profileRepository.UpdateAsync(profile);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
