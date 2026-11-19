using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Application;
using Shared.Infrastructure.API.Hosting;

namespace Shared.Infrastructure.API.Security;

public static class AuthenticationServiceCollectionExtensions
{
    /// <summary>
    /// Validates bearer access tokens issued by the Auth service and enables permission policies.
    /// </summary>
    /// <remarks>
    /// Tokens are signed with Auth's private key and verified with the public keys it publishes
    /// (JWKS), found through <c>Authentication:Schemes:Bearer:Authority</c>; audience and the other
    /// standard JwtBearer settings come from the same section. Claim names are kept as issued
    /// (<c>sub</c>, <c>permission</c>) — no mapping to XML claim types. Only ES256-signed tokens of type
    /// <c>at+jwt</c> are accepted, so neither another algorithm nor a JWT issued for another purpose
    /// passes. Without a key source the application does not start.
    /// </remarks>
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = NetBoxPlusClaimTypes.Subject;
                options.TokenValidationParameters.RoleClaimType = NetBoxPlusClaimTypes.Role;

                // Access tokens live 15 minutes; the default 5-minute skew would extend that by a third.
                options.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);

                options.TokenValidationParameters.ValidAlgorithms = [AccessTokenFormat.Algorithm];
                options.TokenValidationParameters.ValidTypes = [AccessTokenFormat.Type];
            });

        if (!BuildTimeOpenApi.IsGeneratingDocument)
        {
            services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
                .Validate(
                    HasSigningKeySource,
                    "Authentication:Schemes:Bearer:Authority is not configured, so access tokens cannot be validated.")
                .ValidateOnStart();
        }

        services.AddAuthorization();
        services.Replace(ServiceDescriptor.Singleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthorizationHandler, PermissionAuthorizationHandler>());

        services.AddHttpContextAccessor();
        services.TryAddScoped<ICurrentUser, HttpContextCurrentUser>();

        return services;
    }

    /// <summary>Requires the caller to hold <paramref name="permission"/>.</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.RequireAuthorization(PermissionPolicyProvider.PolicyName(permission));
    }

    /// <summary>JwtBearer turns an Authority into a configuration manager; tests and the auth service set keys directly.</summary>
    private static bool HasSigningKeySource(JwtBearerOptions options)
        => options.ConfigurationManager is not null
            || options.Configuration is not null
            || options.TokenValidationParameters.IssuerSigningKey is not null
            || options.TokenValidationParameters.IssuerSigningKeys?.Any() == true
            || options.TokenValidationParameters.IssuerSigningKeyResolver is not null;
}
