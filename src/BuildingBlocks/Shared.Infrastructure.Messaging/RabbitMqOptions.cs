namespace Shared.Infrastructure.Messaging;

/// <summary>Settings of the RabbitMQ connection and consumer. Bound from the <c>Messaging</c> section.</summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "Messaging";

    /// <summary>AMQP URI, for example <c>amqp://user:password@localhost:5672/</c>. Keep it in user secrets or the environment.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// This service's name in lower-case kebab form (for example <c>notification</c>). Its queue is
    /// <c>netboxplus.{ServiceName}</c>.
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>How many unacknowledged messages the broker hands this consumer at once.</summary>
    public ushort PrefetchCount { get; set; } = 16;

    /// <summary>
    /// Waits between attempts when a handler throws. After the last one the message is dead-lettered.
    /// When none are configured, <see cref="DefaultRetryDelays"/> apply.
    /// </summary>
    /// <remarks>
    /// Starts empty on purpose: the configuration binder appends to an array that already has
    /// elements, so a default here could never be replaced from configuration.
    /// </remarks>
    public TimeSpan[] RetryDelays { get; set; } = [];

    public static IReadOnlyList<TimeSpan> DefaultRetryDelays { get; } =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)];
}
