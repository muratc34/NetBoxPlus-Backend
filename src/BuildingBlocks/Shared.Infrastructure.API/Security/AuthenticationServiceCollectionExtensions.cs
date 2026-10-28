using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Application;

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
    /// (<c>sub</c>, <c>permission</c>) — no mapping to XML claim types.
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
            });

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
}
