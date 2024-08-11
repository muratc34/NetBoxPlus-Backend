using Auth.Application.Abstractions;
using Auth.Domain.Profiles;
using Domain.Core.Errors;
using Shared.Application.Abstractions;
using Shared.Domain.Result;
using Shared.UnitOfWork;

namespace Auth.Application.Profiles.CreateProfile;

public sealed class CreateProfileCommandHandler : ICommandHandler<CreateProfileCommand, Result>
{
    private readonly IProfileRepository _profileRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IUserContext _userContext;

    public CreateProfileCommandHandler(
        IProfileRepository profileRepository, 
        IUnitOfWork unitOfWork, 
        IPasswordHasher passwordHasher, 
        IUserContext userContext)
    {
        _profileRepository = profileRepository;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _userContext = userContext;
    }

    public async Task<Result> Handle(CreateProfileCommand request, CancellationToken cancellationToken)
    {
        if (!_userContext.IsAuthenticated)
        {
            return Result.Failure(DomainErrors.User.InvalidPermissions);
        }

        Result<ProfileName> profileNameResult = ProfileName.Create(request.ProfileName);
        Profile profile;

        if (request.Pin is not null)
        {
            Result<Pin> pinResult = Pin.Create(request.Pin);
            Result firstFailureOrSuccess = Result.FirstFailureOrSuccess(profileNameResult, pinResult);

            if (!firstFailureOrSuccess.IsSuccess)
            {
                return Result.Failure(firstFailureOrSuccess.Error!);
            }
            byte[] pinHash, pinSalt;
            _passwordHasher.CreatePasswordHash(pinResult.Data!.Value, out pinHash, out pinSalt);
             profile = Profile.Create(_userContext.UserId, profileNameResult.Data!, pinHash, pinSalt);
        }
        else
        {
            if (!profileNameResult.IsSuccess)
            {
                return Result.Failure(profileNameResult.Error!);
            }
            profile = Profile.Create(_userContext.UserId, profileNameResult.Data!, null, null);
        }

        await _profileRepository.CreateAsync(profile);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
