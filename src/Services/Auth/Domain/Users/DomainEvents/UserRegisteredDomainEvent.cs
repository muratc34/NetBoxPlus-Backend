using Shared.Domain.Event;

namespace Auth.Domain.Users.DomainEvents
{
    public sealed class UserRegisteredDomainEvent : IDomainEvent
    {
        internal UserRegisteredDomainEvent(User user) => User = user;
        public User User { get; }
    }
}
