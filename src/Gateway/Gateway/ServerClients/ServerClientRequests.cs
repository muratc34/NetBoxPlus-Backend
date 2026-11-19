using System.Net;
using Gateway.Routing;
using Gateway.Sessions;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Model;

namespace Gateway.ServerClients;

/// <summary>Who a request is counted against by the rate limiter, as the <c>client</c> metric tag spells it.</summary>
internal static class RateLimitClients
{
    /// <summary>The connection's own address: a browser or any other client calling the gateway itself.</summary>
    public const string Direct = "direct";

    /// <summary>A visitor of the web application, named by its server.</summary>
    public const string Forwarded = "forwarded";

    /// <summary>The web application's server with no visitor behind the request, such as a page rendered ahead of time.</summary>
    public const string Server = "server";
}

/// <summary>
/// The rate limit partition of a request. A visitor named by the web server has a partition apart
/// from the same address calling directly: only the server's partitions count against its total.
/// </summary>
internal sealed record RateLimitClient(string Kind, string Partition)
{
    public static readonly RateLimitClient Server = new(RateLimitClients.Server, "server");

    public static RateLimitClient Direct(IPAddress? address)
        => new(RateLimitClients.Direct, address is null ? "unknown" : PartitionOf(address));

    public static RateLimitClient Forwarded(IPAddress address) => new(RateLimitClients.Forwarded, "visitor:" + Normalized(address));

    private static string PartitionOf(IPAddress address) => "ip:" + Normalized(address);

    // A dual-stack socket shows an IPv4 client as "::ffff:10.0.0.1"; it is the same client as "10.0.0.1".
    private static IPAddress Normalized(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}

/// <summary>
/// Recognises the web application's server on public routes. A request with the server key is counted
/// against the visitor in <c>X-Forwarded-For</c> (exactly one address), or against the server when
/// there is none. A wrong key is refused with <c>401</c>; without the key <c>X-Forwarded-For</c> means
/// nothing, so a browser cannot pick its own partition. The key never reaches a service, and admin,
/// webhook and session endpoints ignore it.
/// </summary>
internal static class ServerClientRequests
{
    public const string KeyHeader = "X-NetBoxPlus-Server-Key";

    public const string ForwardedForHeader = "X-Forwarded-For";

    private static readonly object ClientItem = new();

    public static IServiceCollection AddServerClients(this IServiceCollection services)
    {
        services.AddOptions<ServerClientOptions>()
            .BindConfiguration(ServerClientOptions.SectionName)
            .Validate(options => options.IsValid, $"{ServerClientOptions.SectionName}:Key needs at least {ServerClientOptions.MinimumKeyLength} characters.")
            .ValidateOnStart();

        return services;
    }

    /// <summary>Between routing and the rate limiter: the limiter needs to know who the request is for.</summary>
    public static IApplicationBuilder UseServerClients(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.Use(InvokeAsync);
    }

    /// <summary>The request's partition: the one the server named, otherwise the connection's address.</summary>
    public static RateLimitClient Identify(HttpContext context)
        => context.Items.TryGetValue(ClientItem, out var client) && client is RateLimitClient named
            ? named
            : RateLimitClient.Direct(context.Connection.RemoteIpAddress);

    private static async Task InvokeAsync(HttpContext context, Func<Task> next)
    {
        var headers = context.Request.Headers;
        if (!headers.TryGetValue(KeyHeader, out var presented))
        {
            await next().ConfigureAwait(false);
            return;
        }

        // Whatever the route, the key goes no further than the gateway.
        headers.Remove(KeyHeader);

        if (RouteGroupOf(context) != RouteGroups.Public)
        {
            await next().ConfigureAwait(false);
            return;
        }

        var options = context.RequestServices.GetRequiredService<IOptions<ServerClientOptions>>().Value;
        // A repeated header reads as "key,key" and matches nothing.
        if (!options.IsConfigured || !SessionSecrets.AreEqual(presented.ToString(), options.Key!))
        {
            await GatewayProblem.ServerKeyInvalid.ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        context.Items[ClientItem] = VisitorOf(headers) is { } visitor ? RateLimitClient.Forwarded(visitor) : RateLimitClient.Server;
        await next().ConfigureAwait(false);
    }

    // The server writes the one address it received the page request from. Anything else (a list
    // appended by a proxy in between, a name, nothing) does not parse and names no visitor.
    private static IPAddress? VisitorOf(IHeaderDictionary headers)
        => IPAddress.TryParse(headers[ForwardedForHeader].ToString().Trim(), out var address) ? address : null;

    // Only proxied routes carry a group; the gateway's own endpoints (/session/...) have none.
    private static string? RouteGroupOf(HttpContext context)
        => context.GetEndpoint()?.Metadata.GetMetadata<RouteModel>()?.Config.Metadata?.GetValueOrDefault(RouteGroups.MetadataKey);
}
