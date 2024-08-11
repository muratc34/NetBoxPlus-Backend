using Shared.Domain.Event;

namespace Auth.Domain.Users.DomainEvents
{
    public sealed class UserCreatedDomainEvent : IDomainEvent
    {
        internal UserCreatedDomainEvent(User user) => User = user;
        public User User { get; }
    }
}
