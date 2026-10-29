using System.Diagnostics;
using Gateway.Cors;
using Gateway.RateLimiting;
using Gateway.Routing;
using Gateway.Tracing;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using Yarp.ReverseProxy.Transforms;

namespace Gateway.Hosting;

internal static class GatewayExtensions
{
    public const string ReverseProxySectionName = "ReverseProxy";

    /// <summary>
    /// The reverse proxy with its routes from the <c>ReverseProxy</c> section, the per-group CORS and
    /// rate limiter policies, and the trust boundary for trace context.
    /// </summary>
    public static WebApplicationBuilder AddGateway(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddGatewayCors();
        builder.Services.AddGatewayRateLimiting();

        // ASP.NET Core reads the incoming trace context through this propagator.
        builder.Services.AddSingleton<DistributedContextPropagator, UntrustedTraceContextPropagator>();

        // The OpenTelemetry ASP.NET Core instrumentation re-reads the headers itself unless its
        // propagator is the plain W3C one, which leaves the extraction to ASP.NET Core above.
        Sdk.SetDefaultTextMapPropagator(new TraceContextPropagator());

        builder.Services.AddReverseProxy()
            .LoadFromConfig(builder.Configuration.GetSection(ReverseProxySectionName))
            .AddConfigFilter<RouteGroupConfigFilter>()
            .AddTransforms(context =>
            {
                // The client's trace headers are copied like any other; the gateway's own are added
                // when the request is sent.
                foreach (var header in UntrustedTraceContextPropagator.Headers)
                {
                    context.AddRequestHeaderRemove(header);
                }

                // Services return paths of their own ("/notes/1"); the client needs the gateway's.
                if (RoutePrefixes.TryParse(context.Route, out var prefixes))
                {
                    context.AddResponseTransform(transform =>
                    {
                        var headers = transform.HttpContext.Response.Headers;
                        if (headers.Location.ToString() is { Length: > 0 } location && prefixes.ToGatewayPath(location) is { } rewritten)
                        {
                            headers.Location = rewritten;
                        }

                        return ValueTask.CompletedTask;
                    });
                }
            });

        return builder;
    }

    public static IEndpointRouteBuilder MapGateway(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapReverseProxy(proxy =>
        {
            proxy.Use(PublicRouteGuard.InvokeAsync);
            proxy.UseSessionAffinity();
            proxy.UseLoadBalancing();
            proxy.UsePassiveHealthChecks();
        });

        return endpoints;
    }
}
