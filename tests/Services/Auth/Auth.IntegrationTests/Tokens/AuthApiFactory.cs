using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Auth.IntegrationTests.Tokens;

/// <summary>
/// The auth API in memory with the given signing keys. Nothing here touches the database or the
/// broker, so both get placeholder settings and the background workers that would connect to them
/// are removed.
/// </summary>
internal sealed class AuthApiFactory(string? activeKeyId, params (string KeyId, string Pem)[] keys)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not "Development": that would load the developer's user secrets, signing key included.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Database", "Host=localhost;Port=1;Database=auth;Username=auth;Password=unused");
        builder.UseSetting("Messaging:ConnectionString", "amqp://localhost:1/");

        if (activeKeyId is not null)
        {
            builder.UseSetting("Auth:Tokens:ActiveKeyId", activeKeyId);
        }

        foreach (var (keyId, pem) in keys)
        {
            builder.UseSetting($"Auth:Tokens:SigningKeys:{keyId}", pem);
        }

        builder.ConfigureTestServices(services =>
        {
            var workers = services
                .Where(service => service.ServiceType == typeof(IHostedService)
                    && service.ImplementationType?.Namespace?.StartsWith("Shared.Infrastructure.", StringComparison.Ordinal) == true)
                .ToList();
            foreach (var worker in workers)
            {
                services.Remove(worker);
            }
        });
    }
}
