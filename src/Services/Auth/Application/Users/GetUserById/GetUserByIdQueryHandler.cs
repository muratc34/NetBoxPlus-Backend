using Auth.Application.Abstractions;
using Auth.Domain.Profiles;
using Auth.Domain.Users;
using Domain.Core.Errors;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Abstractions;
using Shared.Domain.Result;

namespace Auth.Application.Users.GetUserById;

public sealed class GetUserByIdQueryHandler : IQueryHandler<GetUserByIdQuery, Result<GetUserByIdResponse>>
{
    private readonly IUserContext _userContext;
    private readonly IUserRepository _userRepository;
    private readonly IProfileRepository _profileRepository;

    public GetUserByIdQueryHandler(
        IUserContext userContext, 
        IUserRepository userRepository, 
        IProfileRepository profileRepository)
    {
        _userContext = userContext;
        _userRepository = userRepository;
        _profileRepository = profileRepository;
    }

    public async Task<Result<GetUserByIdResponse>> Handle(GetUserByIdQuery request, CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty || request.UserId != _userContext.UserId)
        {
            return Result.Failure<GetUserByIdResponse>(DomainErrors.User.InvalidPermissions);
        }

        var response = await _userRepository.FindAll()
            .Where(x => x.Id == request.UserId)
            .Select(user => new GetUserByIdResponse
            {
                Id = user.Id,
                SubscriptionId = user.SubscriptionId,
                PaymentId = user.PaymentId,
                CreatedOnUtc = user.CreatedOnUtc,
                Email = user.Email.Value,
                FullName = user.FirstName.Value + " " + user.LastName.Value,
                FirstName = user.FirstName.Value,
                LastName = user.LastName.Value
            }).SingleOrDefaultAsync(cancellationToken);

        if (response is null)
        {
            return Result.Success(response!);
        }

        response.Profiles = await _profileRepository.FindAll()
            .Where(x => x.UserId == request.UserId)
            .Select(profile => new GetProfileByUserIdResponse
            {
                Id = profile.Id,
                CreatedOnUtc = profile.CreatedOnUtc,
                ProfileName = profile.ProfileName.Value
            }).ToArrayAsync(cancellationToken); ;
        return response;
    }
}
