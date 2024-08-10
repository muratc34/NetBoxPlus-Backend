using Shared.Domain.Event;
using Shared.Domain.Utility;

namespace Shared.Domain;

public abstract class Entity : IEquatable<Entity>
{
    private readonly List<IDomainEvent> _domainEvents = new();

    protected Entity(Guid id) : this()
    {
        Ensure.NotEmpty(id, "The identifier is required", nameof(id));
        Id = id;
    }

    protected Entity()
    {
    }

    public Guid Id { get; private set; }

    public static bool operator == (Entity a, Entity b)
    {
        if (a is null && b is null)
            return true;

        if (a is null || b is null)
            return false;

        return a.Equals(b);
    }

    public static bool operator !=(Entity a, Entity b) => !(a == b);

    public bool Equals(Entity other)
    {
        if (other is null)
            return false;

        return ReferenceEquals(this, other) || Id == other.Id;
    }

    public override bool Equals(object obj)
    {
        if (obj is null)
            return false;

        if (ReferenceEquals(this, obj))
            return true;

        if (obj.GetType() != GetType())
            return false;

        if (!(obj is Entity other))
            return false;

        if (Id == Guid.Empty || other.Id == Guid.Empty)
            return false;

        return Id == other.Id;
    }

    public override int GetHashCode() => Id.GetHashCode() * 41;

    public IReadOnlyList<IDomainEvent> GetDomainEvents()
    {
        return _domainEvents.ToList();
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }

    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }
}
