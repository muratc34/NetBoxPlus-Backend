using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Shared.Application.Tests;

/// <summary>
/// Builds a container the way a service would: AddCqrs over an assembly (this test assembly, whose
/// requests and handlers live in <see cref="TestRequests"/>), plus fakes for the unit of work.
/// </summary>
internal static class TestHost
{
    public static ServiceProvider Build(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();

        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddScoped<CallLog>();
        services.AddScoped<FakeUnitOfWork>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<FakeUnitOfWork>());

        services.AddCqrs(typeof(TestHost).Assembly);
        configure?.Invoke(services);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}

/// <summary>Records what ran, in order, within one scope.</summary>
internal sealed class CallLog
{
    public List<string> Entries { get; } = [];

    public void Add(string entry) => Entries.Add(entry);
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}
