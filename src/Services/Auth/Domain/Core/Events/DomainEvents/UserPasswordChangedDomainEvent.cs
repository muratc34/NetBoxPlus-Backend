using Domain.Entities;

namespace Domain.Core.Events.DomainEvents
{
    public sealed class UserPasswordChangedDomainEvent : IDomainEvent
    {
        internal UserPasswordChangedDomainEvent(User user) => User = user;
        public User User { get; }
    }
}
