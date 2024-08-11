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
    private readonly IUserContext _userContext;

    public CreateProfileCommandHandler(
        IProfileRepository profileRepository, 
        IUnitOfWork unitOfWork,
        IUserContext userContext)
    {
        _profileRepository = profileRepository;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
    }

    public async Task<Result> Handle(CreateProfileCommand request, CancellationToken cancellationToken)
    {
        if (!_userContext.IsAuthenticated)
        {
            return Result.Failure(DomainErrors.User.InvalidPermissions);
        }

        Result<ProfileName> profileNameResult = ProfileName.Create(request.ProfileName);

        if (!profileNameResult.IsSuccess)
        {
            return Result.Failure(profileNameResult.Error!);
        }
        var profile = Profile.Create(_userContext.UserId, profileNameResult.Data!);

        await _profileRepository.CreateAsync(profile);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
