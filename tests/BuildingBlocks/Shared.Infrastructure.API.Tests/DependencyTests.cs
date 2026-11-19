using Shared.Infrastructure.API.Hosting;

namespace Shared.Infrastructure.API.Tests;

/// <summary>
/// The gateway references this project, so it must not pull in persistence or messaging libraries.
/// </summary>
public sealed class DependencyTests
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "RabbitMQ",
        "Shared.Infrastructure.Persistence",
        "Shared.Infrastructure.Messaging",
    ];

    [Fact]
    public void SharedInfrastructureApi_DoesNotReferencePersistenceOrMessaging()
    {
        var references = typeof(ServiceDefaultsExtensions).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .ToList();

        Assert.All(references, name => Assert.DoesNotContain(
            ForbiddenPrefixes,
            prefix => name.StartsWith(prefix, StringComparison.Ordinal)));
    }
}
