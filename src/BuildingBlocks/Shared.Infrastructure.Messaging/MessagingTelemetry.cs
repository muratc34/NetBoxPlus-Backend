using System.Diagnostics;
using Shared.Application.Messaging;

namespace Shared.Infrastructure.Messaging;

/// <summary>
/// Spans for publishing and processing. Both continue the trace stored with the message, so the
/// request that published an event and the handler that processed it share one trace.
/// </summary>
internal static class MessagingTelemetry
{
    public const string SourceName = "Shared.Infrastructure.Messaging";
    public const string TraceParentHeader = "traceparent";

    private static readonly ActivitySource Source = new(SourceName);

    public static Activity? StartPublish(MessageEnvelope envelope, string exchange)
    {
        var activity = Source.StartActivity($"{envelope.MessageType} publish", ActivityKind.Producer, ParentOf(envelope.TraceParent));
        Tag(activity, envelope.MessageType, envelope.MessageId.ToString(), exchange);
        return activity;
    }

    public static Activity? StartProcess(string messageType, string? messageId, string? traceParent, string queue)
    {
        var activity = Source.StartActivity($"{messageType} process", ActivityKind.Consumer, ParentOf(traceParent));
        Tag(activity, messageType, messageId, queue);
        return activity;
    }

    private static ActivityContext ParentOf(string? traceParent)
        => ActivityContext.TryParse(traceParent, traceState: null, out var parent) ? parent : default;

    private static void Tag(Activity? activity, string messageType, string? messageId, string destination)
    {
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination.name", destination);
        activity?.SetTag("messaging.message.id", messageId);
        activity?.SetTag("messaging.message.type", messageType);
    }
}
