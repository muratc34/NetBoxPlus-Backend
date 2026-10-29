using System.Collections.Concurrent;
using System.Diagnostics;
using Shared.Application.Messaging;
using Shared.Domain;

// Contracts must live in "<Service>.Contracts"; these are published to netboxplus.messagingtests.events.
namespace MessagingTests.Contracts
{
    public sealed record OrderPlaced(Guid OrderId, decimal Total);

    public sealed record ShipmentDelayed(Guid OrderId);

    public sealed record PaymentRejected(Guid OrderId, string Reason);
}

namespace Shared.Infrastructure.Messaging.Tests
{
    using MessagingTests.Contracts;

    /// <summary>What the handlers saw, so tests can wait for deliveries.</summary>
    internal sealed class Deliveries
    {
        private readonly ConcurrentQueue<(object Event, ActivityTraceId TraceId)> _received = new();
        private int _shipmentAttempts;

        public IReadOnlyCollection<(object Event, ActivityTraceId TraceId)> Received => _received;

        public int ShipmentAttempts => Volatile.Read(ref _shipmentAttempts);

        public void Record(object integrationEvent)
            => _received.Enqueue((integrationEvent, Activity.Current?.TraceId ?? default));

        public void RecordShipmentAttempt() => Interlocked.Increment(ref _shipmentAttempts);

        public async Task WaitForAsync(int count, TimeSpan timeout)
        {
            using var cancellation = new CancellationTokenSource(timeout);
            while (_received.Count < count)
            {
                await Task.Delay(50, cancellation.Token);
            }
        }
    }

    internal sealed class OrderPlacedHandler(Deliveries deliveries) : IIntegrationEventHandler<OrderPlaced>
    {
        public Task<Result> Handle(OrderPlaced integrationEvent, CancellationToken cancellationToken)
        {
            deliveries.Record(integrationEvent);
            return Task.FromResult(Result.Success());
        }
    }

    internal sealed class ShipmentDelayedHandler(Deliveries deliveries) : IIntegrationEventHandler<ShipmentDelayed>
    {
        public Task<Result> Handle(ShipmentDelayed integrationEvent, CancellationToken cancellationToken)
        {
            deliveries.RecordShipmentAttempt();
            throw new InvalidOperationException("The carrier API is down.");
        }
    }

    internal sealed class PaymentRejectedHandler(Deliveries deliveries) : IIntegrationEventHandler<PaymentRejected>
    {
        public Task<Result> Handle(PaymentRejected integrationEvent, CancellationToken cancellationToken)
        {
            deliveries.Record(integrationEvent);
            return Task.FromResult(Result.Failure(Error.Conflict("Order.AlreadyCancelled", "The order is already cancelled.")));
        }
    }

    /// <summary>The persistence layer's role in tests: remembers which messages each consumer processed.</summary>
    internal sealed class InMemoryInbox : IInbox
    {
        private readonly ConcurrentDictionary<(Guid, string), bool> _processed = new();

        public async Task<bool> ProcessOnceAsync(
            Guid messageId,
            string consumer,
            Func<CancellationToken, Task<Result>> work,
            CancellationToken cancellationToken = default)
        {
            if (_processed.ContainsKey((messageId, consumer)))
            {
                return false;
            }

            await work(cancellationToken);
            return _processed.TryAdd((messageId, consumer), true);
        }
    }
}
