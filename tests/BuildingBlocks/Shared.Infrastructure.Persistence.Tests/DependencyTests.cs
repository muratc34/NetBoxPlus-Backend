namespace Shared.Infrastructure.Persistence.Tests;

/// <summary>Persistence and messaging meet only through Shared.Application interfaces.</summary>
public sealed class DependencyTests
{
    private static readonly string[] ForbiddenPrefixes = ["RabbitMQ", "Shared.Infrastructure.Messaging", "Shared.Infrastructure.API", "Microsoft.AspNetCore"];

    [Fact]
    public void SharedInfrastructurePersistence_DoesNotReferenceMessagingOrAspNetCore()
    {
        var references = typeof(ServiceDbContext).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .ToList();

        Assert.All(references, name => Assert.DoesNotContain(
            ForbiddenPrefixes,
            prefix => name.StartsWith(prefix, StringComparison.Ordinal)));
    }
}
