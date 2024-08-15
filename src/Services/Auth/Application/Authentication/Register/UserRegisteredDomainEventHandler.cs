using Auth.Domain.Profiles;
using Auth.Domain.Users.DomainEvents;
using Shared.Application.Abstractions;
using Shared.UnitOfWork;

namespace Auth.Application.Authentication.Register;

public sealed class UserRegisteredDomainEventHandler : IDomainEventHandler<UserRegisteredDomainEvent>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProfileRepository _profileRepository;

    public UserRegisteredDomainEventHandler(IUnitOfWork unitOfWork, IProfileRepository profileRepository)
    {
        _unitOfWork = unitOfWork;
        _profileRepository = profileRepository;
    }

    public async Task Handle(UserRegisteredDomainEvent notification, CancellationToken cancellationToken)
    {
        var profile = new Profile(notification.User.Id, new ProfileName(notification.User.FirstName.Value));

        await _profileRepository.CreateAsync(profile);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
