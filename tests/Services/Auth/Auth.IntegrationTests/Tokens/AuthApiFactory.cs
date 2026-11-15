using Auth.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetBoxPlus.Testing;

namespace Auth.IntegrationTests.Tokens;

/// <summary>
/// The auth API in memory with the given signing keys. The broker is never reached: the background
/// workers (outbox processor, consumer) are removed, so outbox rows stay in the table for tests to
/// read. Without <see cref="Database"/> the database is a placeholder too.
/// </summary>
internal sealed class AuthApiFactory(string? activeKeyId, params (string KeyId, string Pem)[] keys)
    : WebApplicationFactory<Program>
{
    private readonly TaskCompletionSource _startObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);

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
            services.RetryLostConnectionAttempts<AuthDbContext>();

            var workers = services
                .Where(service => service.ServiceType == typeof(IHostedService)
                    && service.ImplementationType?.Namespace?.StartsWith("Shared.Infrastructure.", StringComparison.Ordinal) == true)
                .ToList();
            foreach (var worker in workers)
            {
                services.Remove(worker);
            }

            var lifetime = services.Last(service => service.ServiceType == typeof(IHostLifetime));
            services.Remove(lifetime);
            services.AddSingleton<IHostLifetime>(provider => new GatedHostLifetime(Create(provider, lifetime), _startObserved.Task));
        });
    }

    /// <summary>
    /// With a top-level Program the application starts the host itself (<c>app.Run()</c>), and the
    /// factory only waits for it: <c>Start</c> subscribes to <c>ApplicationStarted</c> through the
    /// host's services. If the start fails first, <c>Run</c> disposes the host, and that subscription
    /// throws <see cref="ObjectDisposedException"/> in place of the real failure. So the application
    /// waits (see <see cref="GatedHostLifetime"/>) until the factory has subscribed, and a failed
    /// start always surfaces as its own exception.
    /// </summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var host = builder.Build();
        Task start;
        try
        {
            // Subscribes before its first await.
            start = host.StartAsync();
        }
        finally
        {
            _startObserved.TrySetResult();
        }

        start.GetAwaiter().GetResult();
        return host;
    }

    private static IHostLifetime Create(IServiceProvider provider, ServiceDescriptor descriptor) =>
        (IHostLifetime)(descriptor.ImplementationInstance
            ?? descriptor.ImplementationFactory?.Invoke(provider)
            ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!));

    /// <summary>Holds the application's start until the factory is ready to observe it.</summary>
    private sealed class GatedHostLifetime(IHostLifetime inner, Task gate) : IHostLifetime
    {
        public async Task WaitForStartAsync(CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            await inner.WaitForStartAsync(cancellationToken).ConfigureAwait(false);
        }

        public Task StopAsync(CancellationToken cancellationToken) => inner.StopAsync(cancellationToken);
    }
}
