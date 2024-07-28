using Domain.Entities;

namespace Domain.Core.Events.DomainEvents
{
    public sealed class UserCreatedDomainEvent : IDomainEvent
    {
        internal UserCreatedDomainEvent(User user) => User = user;
        public User User { get; }
    }
}
