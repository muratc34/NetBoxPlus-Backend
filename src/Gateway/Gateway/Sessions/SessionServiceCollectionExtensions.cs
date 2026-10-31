using Microsoft.AspNetCore.DataProtection;
using Shared.Infrastructure.API.Hosting;

namespace Gateway.Sessions;

internal static class SessionServiceCollectionExtensions
{
    public const string ValkeyConnectionStringName = "Valkey";

    /// <summary>The Data Protection key ring in Valkey, so every gateway instance can read every session.</summary>
    private const string DataProtectionKey = "gateway:data-protection-keys";

    /// <summary>
    /// Browser sessions in Valkey (<c>ConnectionStrings:Valkey</c>), encrypted with Data Protection,
    /// and the client for the auth service (<c>Session:AuthAddress</c>). The gateway does not start
    /// without either setting; it does start while Valkey is unreachable.
    /// </summary>
    /// <remarks>
    /// The keys live next to the sessions they protect, which guards against a leaked dump of the
    /// sessions alone but not against someone in control of Valkey. Protecting the key ring with a
    /// certificate (<c>ProtectKeysWithCertificate</c>) is a deployment step.
    /// </remarks>
    public static IServiceCollection AddGatewaySessions(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ValkeyConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{ValkeyConnectionStringName} is not configured. Locally, run ./deploy/dev.ps1 secrets.");
        }

        var valkey = new ValkeyConnection(connectionString);
        services.AddSingleton(_ => valkey);

        services.AddDataProtection()
            .SetApplicationName("NetBoxPlus.Gateway")
            .PersistKeysToStackExchangeRedis(() => valkey.Database, DataProtectionKey);

        services.AddOptions<SessionOptions>()
            .BindConfiguration(SessionOptions.SectionName)
            .Validate(
                options => options.IsValid,
                $"{SessionOptions.SectionName}: AuthAddress must be an absolute URI and every duration positive.")
            .ValidateOnStart();

        services.AddHttpClient(AuthClient.HttpClientName, (provider, client) =>
        {
            client.BaseAddress = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SessionOptions>>().Value.AuthAddress;
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddSingleton<AuthClient>();
        services.AddSingleton<SessionStore>();
        services.AddSingleton<SessionCookies>();
        services.AddSingleton<SessionManager>();

        services.AddHealthChecks().AddCheck<ValkeyHealthCheck>("valkey", tags: [HealthTags.Ready]);

        return services;
    }
}
