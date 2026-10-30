using Auth.Application.Abstractions;
using Auth.Domain.RefreshTokens;
using Auth.Domain.Roles;
using Auth.Domain.Users;
using Auth.Infrastructure.Persistence;
using Auth.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure.Messaging;
using Shared.Infrastructure.Persistence;

namespace Auth.Infrastructure;

public static class AuthInfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers the database (from <c>ConnectionStrings:Database</c>), the repositories and the
    /// read model, password hashing, and RabbitMQ messaging (from the <c>Messaging</c> section).
    /// </summary>
    public static IServiceCollection AddAuthInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(AuthDbContext.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{AuthDbContext.ConnectionStringName} is not configured. Locally, run ./deploy/dev.ps1 secrets.");

        services.AddAuthPersistence(connectionString);
        services.AddRabbitMqMessaging();

        return services;
    }

    /// <summary>Everything but messaging; the integration tests use it on its own.</summary>
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
}
