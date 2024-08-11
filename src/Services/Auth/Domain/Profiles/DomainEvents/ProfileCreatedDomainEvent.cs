using Shared.Domain.Event;

namespace Auth.Domain.Profiles.DomainEvents;

public sealed class ProfileCreatedDomainEvent : IDomainEvent
{
    internal ProfileCreatedDomainEvent(Profile profile) => Profile = profile;
    public Profile Profile { get; }
}
