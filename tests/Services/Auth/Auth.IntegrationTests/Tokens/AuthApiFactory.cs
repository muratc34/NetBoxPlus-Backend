using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Auth.IntegrationTests.Tokens;

/// <summary>
/// The auth API in memory with the given signing keys. The broker is never reached: the background
/// workers (outbox processor, consumer) are removed, so outbox rows stay in the table for tests to
/// read. Without <see cref="Database"/> the database is a placeholder too.
/// </summary>
internal sealed class AuthApiFactory(string? activeKeyId, params (string KeyId, string Pem)[] keys)
    : WebApplicationFactory<Program>
{
    /// <summary>A real database for tests that need one; the default points nowhere.</summary>
    public string Database { get; init; } = "Host=localhost;Port=1;Database=auth;Username=auth;Password=unused";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not "Development": that would load the developer's user secrets, signing key included.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Database", Database);
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
