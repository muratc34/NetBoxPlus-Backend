using System.Diagnostics;

namespace Gateway.Tracing;

/// <summary>
/// The gateway is where requests from the internet enter, so it does not continue a trace or
/// baggage sent by the client: every request starts a new trace here. Outgoing requests still carry
/// the gateway's trace context to the services.
/// </summary>
internal sealed class UntrustedTraceContextPropagator : DistributedContextPropagator
{
    /// <summary>Request headers that carry trace context or baggage, in any of the supported formats.</summary>
    public static readonly string[] Headers = ["traceparent", "tracestate", "baggage", "Correlation-Context", "Request-Id"];

    private readonly DistributedContextPropagator _outgoing = CreateDefaultPropagator();

    public override IReadOnlyCollection<string> Fields => _outgoing.Fields;

    public override void Inject(Activity? activity, object? carrier, PropagatorSetterCallback? setter)
        => _outgoing.Inject(activity, carrier, setter);

    public override void ExtractTraceIdAndState(
        object? carrier,
        PropagatorGetterCallback? getter,
        out string? traceId,
        out string? traceState)
    {
        traceId = null;
        traceState = null;
    }

    public override IEnumerable<KeyValuePair<string, string?>>? ExtractBaggage(object? carrier, PropagatorGetterCallback? getter)
        => null;
}
