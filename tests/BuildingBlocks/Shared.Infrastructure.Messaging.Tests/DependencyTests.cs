namespace Shared.Infrastructure.Messaging.Tests;

/// <summary>Messaging and persistence meet only through Shared.Application interfaces.</summary>
public sealed class DependencyTests
{
    private static readonly string[] ForbiddenPrefixes =
        ["Microsoft.EntityFrameworkCore", "Npgsql", "Shared.Infrastructure.Persistence", "Shared.Infrastructure.API", "Microsoft.AspNetCore"];

    [Fact]
    public void SharedInfrastructureMessaging_DoesNotReferencePersistenceOrAspNetCore()
    {
        var references = typeof(RabbitMqOptions).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .ToList();

        Assert.All(references, name => Assert.DoesNotContain(
            ForbiddenPrefixes,
            prefix => name.StartsWith(prefix, StringComparison.Ordinal)));
    }
}
