using System.Globalization;
using System.Threading.RateLimiting;
using Gateway.Routing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Shared.Infrastructure.API.Errors;

namespace Gateway.RateLimiting;

internal static partial class RateLimitingServiceCollectionExtensions
{
    public const string ExceededCode = "RateLimit.Exceeded";

    /// <summary>
    /// One fixed-window policy per route group, named after the group and partitioned by client IP
    /// address. A rejected request gets <c>429</c> problem details and a <c>Retry-After</c> header.
    /// </summary>
    public static IServiceCollection AddGatewayRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<GatewayRateLimitOptions>()
            .BindConfiguration(GatewayRateLimitOptions.SectionName)
            .Validate(
                options => options.Public.IsValid && options.Admin.IsValid,
                $"{GatewayRateLimitOptions.SectionName}: every limit needs a positive PermitLimit and Window.")
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRejectionAsync;
        });

        services.AddOptions<RateLimiterOptions>().Configure<IOptions<GatewayRateLimitOptions>>((rateLimiter, gateway) =>
        {
            rateLimiter.AddPolicy(RouteGroups.Public, context => Partition(context, gateway.Value.Public));
            rateLimiter.AddPolicy(RouteGroups.Admin, context => Partition(context, gateway.Value.Admin));
        });

        return services;
    }

    // Behind a load balancer this must become the forwarded client address; until then the
    // connection's address is the client.
    private static RateLimitPartition<string> Partition(HttpContext context, FixedWindowLimit limit)
        => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit.PermitLimit,
                Window = limit.Window,
                QueueLimit = 0,
            });

    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            httpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        var logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(RateLimitingServiceCollectionExtensions));
        LogRejected(logger, httpContext.Request.Method, httpContext.Request.Path);

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Rate limit exceeded for {Method} {Path}")]
    private static partial void LogRejected(ILogger logger, string method, string path);
}
