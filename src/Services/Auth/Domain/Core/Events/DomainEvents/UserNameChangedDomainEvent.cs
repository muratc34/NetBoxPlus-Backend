using Domain.Entities;

namespace Domain.Core.Events.DomainEvents
{
    public sealed class UserNameChangedDomainEvent : IDomainEvent
    {
        internal UserNameChangedDomainEvent(User user) => User = user;
        public User User { get; }
    }
}
