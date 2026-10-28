namespace Shared.Domain.Tests;

public sealed class AggregateRootTests
{
    [Fact]
    public void RaiseDomainEvent_CollectsEventsInOrder()
    {
        var order = new Order(Guid.CreateVersion7());

        order.Place();
        order.Cancel();

        Assert.Collection(
            order.DomainEvents,
            e => Assert.IsType<OrderPlaced>(e),
            e => Assert.IsType<OrderCancelled>(e));
    }

    [Fact]
    public void ClearDomainEvents_RemovesAllEvents()
    {
        var order = new Order(Guid.CreateVersion7());
        order.Place();

        order.ClearDomainEvents();

        Assert.Empty(order.DomainEvents);
    }

    [Fact]
    public void DomainEvents_CannotBeModifiedFromOutside()
    {
        var order = new Order(Guid.CreateVersion7());
        order.Place();

        var events = Assert.IsAssignableFrom<ICollection<IDomainEvent>>(order.DomainEvents);
        Assert.True(events.IsReadOnly);
    }

    [Fact]
    public void RaiseDomainEvent_WithNull_Throws()
    {
        var order = new Order(Guid.CreateVersion7());

        Assert.Throws<ArgumentNullException>(order.RaiseNull);
    }

    private sealed record OrderPlaced(Guid OrderId) : IDomainEvent;

    private sealed record OrderCancelled(Guid OrderId) : IDomainEvent;

    private sealed class Order(Guid id) : AggregateRoot(id)
    {
        public void Place() => RaiseDomainEvent(new OrderPlaced(Id));

        public void Cancel() => RaiseDomainEvent(new OrderCancelled(Id));

        public void RaiseNull() => RaiseDomainEvent(null!);
    }
}
