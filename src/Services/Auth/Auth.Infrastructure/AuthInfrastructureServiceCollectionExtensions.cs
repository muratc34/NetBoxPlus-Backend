using Auth.Application.Abstractions;
using Auth.Domain.RefreshTokens;
using Auth.Domain.Roles;
using Auth.Domain.Users;
using Auth.Infrastructure.Persistence;
using Auth.Infrastructure.Security;
using Auth.Infrastructure.Security.Tokens;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Shared.Infrastructure.Messaging;
using Shared.Infrastructure.Persistence;

namespace Auth.Infrastructure;

public static class AuthInfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers the database (from <c>ConnectionStrings:Database</c>), the repositories and the
    /// read model, password hashing, token signing (from the <c>Auth:Tokens</c> section) and RabbitMQ
    /// messaging (from the <c>Messaging</c> section).
    /// </summary>
    public static IServiceCollection AddAuthInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(AuthDbContext.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{AuthDbContext.ConnectionStringName} is not configured. Locally, run ./deploy/dev.ps1 secrets.");

        services.AddAuthPersistence(connectionString);
        services.AddAuthTokens();
        services.AddRabbitMqMessaging();

        return services;
    }

    /// <summary>The database, repositories and password hashing; the integration tests use it on its own.</summary>
    internal static IServiceCollection AddAuthPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddPersistence<AuthDbContext>(connectionString);
        services.AddScoped<IAuthReadModel>(provider => provider.GetRequiredService<AuthDbContext>());
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

        services.Configure<PasswordHasherOptions>(options =>
        {
            options.CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3;
            options.IterationCount = IdentityPasswordHasher.IterationCount;
        });
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();

        return services;
    }

    /// <summary>Signing keys and the access token issuer; the service does not start without a valid key.</summary>
    internal static IServiceCollection AddAuthTokens(this IServiceCollection services)
    {
        services.AddOptions<AccessTokenOptions>()
            .BindConfiguration(AccessTokenOptions.SectionName)
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AccessTokenOptions>, AccessTokenOptionsValidator>());

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<SigningKeyRing>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();

        return services;
    }
}
