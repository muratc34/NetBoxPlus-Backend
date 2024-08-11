using Auth.Domain.Users;
using Shared.Domain.Event;

namespace Auth.Domain.Users.DomainEvents
{
    public sealed class UserPasswordChangedDomainEvent : IDomainEvent
    {
        internal UserPasswordChangedDomainEvent(User user) => User = user;
        public User User { get; }
    }
}
