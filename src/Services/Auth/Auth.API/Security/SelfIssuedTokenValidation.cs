using Auth.Infrastructure.Security.Tokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace Auth.API.Security;

internal static class SelfIssuedTokenValidation
{
    /// <summary>
    /// Other services find the signing keys through <c>Authority</c>; the auth service already
    /// holds them, so it validates its own tokens without calling itself over HTTP.
    /// </summary>
    public static IServiceCollection AddSelfIssuedTokenValidation(this IServiceCollection services)
    {
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<SigningKeyRing, IOptions<AccessTokenOptions>>((options, keys, tokens) =>
            {
                options.TokenValidationParameters.ValidIssuer = tokens.Value.Issuer;
                options.TokenValidationParameters.IssuerSigningKeys = keys.ValidationKeys;
            });

        return services;
    }
}
