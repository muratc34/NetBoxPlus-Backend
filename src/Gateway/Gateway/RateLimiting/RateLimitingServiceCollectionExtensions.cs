using System.Globalization;
using System.Threading.RateLimiting;
using Gateway.Routing;
using Gateway.ServerClients;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Shared.Infrastructure.API.Errors;

namespace Gateway.RateLimiting;

internal static partial class RateLimitingServiceCollectionExtensions
{
    public const string ExceededCode = "RateLimit.Exceeded";

    /// <summary>For endpoints that take a password or send e-mail: sign-in, registration, password change and reset.</summary>
    public const string CredentialsPolicy = "credentials";

    /// <summary>
    /// One fixed-window policy per route group, named after the group, and a stricter one for
    /// endpoints that take a password; all partitioned by client IP address, which for the web
    /// application's server is the visitor it names (<see cref="ServerClientRequests"/>). Everything
    /// the server sends also counts against its total (<see cref="WithinTotalLimiter"/>), so visitors
    /// it was told about wrongly (an address made up in front of it) cannot multiply its load. A
    /// rejected request gets <c>429</c> problem details and a <c>Retry-After</c> header.
    /// </summary>
    public static IServiceCollection AddGatewayRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<GatewayRateLimitOptions>()
            .BindConfiguration(GatewayRateLimitOptions.SectionName)
            .Validate(
                options => options.Public.IsValid && options.Admin.IsValid && options.Credentials.IsValid && options.Webhooks.IsValid && options.Server.IsValid,
                $"{GatewayRateLimitOptions.SectionName}: every limit needs a positive PermitLimit and Window.")
            .ValidateOnStart();

        services.AddSingleton<GatewayMetrics>();
        services.AddSingleton<ServerTotalLimiter>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRejectionAsync;
        });

        services.AddOptions<RateLimiterOptions>().Configure<IOptions<GatewayRateLimitOptions>, ServerTotalLimiter>((rateLimiter, gateway, server) =>
        {
            rateLimiter.AddPolicy(RouteGroups.Public, context => PublicPartition(context, gateway.Value.Public, server.Limiter));
            rateLimiter.AddPolicy(RouteGroups.Admin, context => Partition(context, gateway.Value.Admin));
            rateLimiter.AddPolicy(CredentialsPolicy, context => Partition(context, gateway.Value.Credentials));
            rateLimiter.AddPolicy(RouteGroups.Webhooks, context => Partition(context, gateway.Value.Webhooks));
        });

        return services;
    }

    // Behind a load balancer the connection's address must become the forwarded one; until then it
    // is the client. Only public routes know the web application's server (ServerClientRequests).
    private static RateLimitPartition<string> Partition(HttpContext context, FixedWindowLimit limit)
        => FixedWindow(ServerClientRequests.Identify(context).Partition, limit);

    // A visitor the server names has the public limit of their own and shares the server's total;
    // the server without a visitor has the total alone.
    private static RateLimitPartition<string> PublicPartition(HttpContext context, FixedWindowLimit limit, RateLimiter total)
    {
        var client = ServerClientRequests.Identify(context);
        return client.Kind switch
        {
            RateLimitClients.Forwarded => RateLimitPartition.Get(client.Partition, _ => new WithinTotalLimiter(new FixedWindowRateLimiter(FixedWindowOptions(limit)), total)),
            RateLimitClients.Server => RateLimitPartition.Get(client.Partition, _ => new WithinTotalLimiter(own: null, total)),
            _ => FixedWindow(client.Partition, limit),
        };
    }

    private static RateLimitPartition<string> FixedWindow(string partition, FixedWindowLimit limit)
        => RateLimitPartition.GetFixedWindowLimiter(partition, _ => FixedWindowOptions(limit));

    internal static FixedWindowRateLimiterOptions FixedWindowOptions(FixedWindowLimit limit)
        => new()
        {
            PermitLimit = limit.PermitLimit,
            Window = limit.Window,
            QueueLimit = 0,
        };

    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            httpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        var client = ServerClientRequests.Identify(httpContext).Kind;
        var group = httpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName ?? "unknown";
        httpContext.RequestServices.GetRequiredService<GatewayMetrics>().RateLimitRejected(group, client);

        var logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(RateLimitingServiceCollectionExtensions));
        LogRejected(logger, httpContext.Request.Method, httpContext.Request.Path, group, client);

        var typeBaseUri = httpContext.RequestServices.GetRequiredService<IOptions<ErrorResponseOptions>>().Value.TypeBaseUri;
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = ReasonPhrases.GetReasonPhrase(StatusCodes.Status429TooManyRequests),
            Detail = "Too many requests. Retry after the number of seconds in the Retry-After header.",
            Type = new Uri(typeBaseUri, "rate-limit-exceeded").AbsoluteUri,
        };
        problem.Extensions["code"] = ExceededCode;

        await httpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
        }).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Rate limit exceeded for {Method} {Path} ({Group}, {Client})")]
    private static partial void LogRejected(ILogger logger, string method, string path, string group, string client);
}
