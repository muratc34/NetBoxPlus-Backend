using Auth.Domain.Users;
using Shared.Domain.Event;

namespace Domain.Core.Events.DomainEvents
{
    public sealed class UserPasswordChangedDomainEvent : IDomainEvent
    {
        internal UserPasswordChangedDomainEvent(User user) => User = user;
        public User User { get; }
    }
}
