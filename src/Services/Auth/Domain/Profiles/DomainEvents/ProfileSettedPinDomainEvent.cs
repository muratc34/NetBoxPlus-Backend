using Shared.Domain.Event;

namespace Auth.Domain.Profiles.DomainEvents;

public sealed class ProfileSettedPinDomainEvent : IDomainEvent
{
    internal ProfileSettedPinDomainEvent(Profile profile) => Profile = profile;
    public Profile Profile { get; }
}
