using Auth.Application.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Application;

namespace Auth.Application;

public static class AuthApplicationServiceCollectionExtensions
{
    /// <summary>Registers the dispatcher, every handler and validator of the auth service, and the session token helpers.</summary>
    public static IServiceCollection AddAuthApplication(this IServiceCollection services)
    {
        services.AddCqrs(typeof(AuthApplicationServiceCollectionExtensions).Assembly);

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<SessionTokens>();
        services.AddSingleton<UnknownUserPassword>();

        return services;
    }
}
