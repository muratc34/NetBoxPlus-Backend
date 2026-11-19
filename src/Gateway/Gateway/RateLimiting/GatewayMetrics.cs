using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Gateway.RateLimiting;

/// <summary>
/// The <c>NetBoxPlus.Gateway</c> meter. Tags carry the route group and the kind of client, never an
/// address.
/// </summary>
public sealed class GatewayMetrics
{
    public const string MeterName = "NetBoxPlus.Gateway";

    private readonly Counter<long> _rateLimitRejections;

    public GatewayMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(MeterName);
        _rateLimitRejections = meter.CreateCounter<long>(
            "netboxplus.gateway.rate_limit.rejections",
            unit: "{request}",
            description: "Requests refused with 429 by the rate limiter.");
    }

    /// <param name="group">The rate limiter policy: a route group or <c>credentials</c>.</param>
    /// <param name="client"><c>direct</c>, <c>forwarded</c> or <c>server</c>.</param>
    public void RateLimitRejected(string group, string client)
        => _rateLimitRejections.Add(1, new TagList { { "group", group }, { "client", client } });
}
