namespace Shared.Infrastructure.Messaging;

/// <summary>
/// Names of exchanges and queues, derived from message types and service names so that no service
/// needs to configure another's. A contract in <c>Billing.Contracts</c> is published to the
/// <c>netboxplus.billing.events</c> topic exchange with its message type as routing key; each
/// consuming service has one queue bound to the types it handles, plus a dead-letter queue.
/// </summary>
internal static class RabbitMqTopology
{
    public const string DeadLetterExchange = "netboxplus.dead-letter";

    private const string ContractsSegment = "Contracts";

    /// <summary><c>Billing.Contracts.SubscriptionCreated</c> → <c>netboxplus.billing.events</c>.</summary>
    public static string ExchangeFor(string messageType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);

        var segments = messageType.Split('.');
        if (segments.Length < 3 || segments[1] != ContractsSegment || string.IsNullOrWhiteSpace(segments[0]))
        {
            throw new InvalidOperationException(
                $"Integration event '{messageType}' must live in the '<Service>.{ContractsSegment}' namespace of its publishing service.");
        }

        return $"netboxplus.{segments[0].ToLowerInvariant()}.events";
    }

    public static string QueueFor(string serviceName) => $"netboxplus.{serviceName}";

    public static string DeadLetterQueueFor(string serviceName) => $"{QueueFor(serviceName)}.dead-letter";
}
