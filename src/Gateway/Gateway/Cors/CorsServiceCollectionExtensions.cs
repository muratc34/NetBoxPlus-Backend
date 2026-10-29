using Gateway.Routing;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

namespace Gateway.Cors;

internal static class CorsServiceCollectionExtensions
{
    private static readonly string[] AllowedMethods = ["GET", "POST", "PUT", "PATCH", "DELETE"];
    private static readonly string[] AllowedHeaders = ["Content-Type", "Authorization", "Idempotency-Key"];
    private static readonly string[] ExposedHeaders = ["Location", "Retry-After"];

    /// <summary>One CORS policy per route group, named after the group.</summary>
    public static IServiceCollection AddGatewayCors(this IServiceCollection services)
    {
        services.AddOptions<GatewayCorsOptions>()
            .BindConfiguration(GatewayCorsOptions.SectionName)
            .Validate(
                options => options.PublicOrigins.Concat(options.AdminOrigins).All(GatewayCorsOptions.IsValidOrigin),
                $"{GatewayCorsOptions.SectionName}: every origin must be an absolute http(s) URI without a path or trailing slash.")
            .ValidateOnStart();

        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<IOptions<GatewayCorsOptions>>((cors, gateway) =>
        {
            cors.AddPolicy(RouteGroups.Public, policy => Configure(policy, gateway.Value.PublicOrigins));
            cors.AddPolicy(RouteGroups.Admin, policy => Configure(policy, gateway.Value.AdminOrigins));
        });

        return services;
    }

    // Credentials are allowed for the session cookie the gateway will issue; that is also why the
    // origins are listed and never "*".
    private static void Configure(CorsPolicyBuilder policy, string[] origins)
        => policy
            .WithOrigins(origins)
            .WithMethods(AllowedMethods)
            .WithHeaders(AllowedHeaders)
            .WithExposedHeaders(ExposedHeaders)
            .AllowCredentials()
            .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
}
