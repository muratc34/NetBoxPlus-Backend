using Microsoft.Extensions.DependencyInjection;
using Shared.Application;

namespace Auth.Application;

public static class AuthApplicationServiceCollectionExtensions
{
    /// <summary>Registers the dispatcher and every handler and validator of the auth service.</summary>
    public static IServiceCollection AddAuthApplication(this IServiceCollection services)
        => services.AddCqrs(typeof(AuthApplicationServiceCollectionExtensions).Assembly);
}
