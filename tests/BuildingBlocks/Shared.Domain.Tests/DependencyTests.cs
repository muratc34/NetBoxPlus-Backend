namespace Shared.Domain.Tests;

/// <summary>
/// Shared.Domain must stay free of third-party dependencies: every service's domain layer
/// references it, so anything it pulls in reaches every domain model.
/// </summary>
public sealed class DependencyTests
{
    [Fact]
    public void SharedDomain_ReferencesOnlyTheBaseClassLibrary()
    {
        var references = typeof(Entity).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .ToList();

        Assert.NotEmpty(references);
        Assert.All(references, name => Assert.True(
            name == "netstandard" || name.StartsWith("System", StringComparison.Ordinal),
            $"Shared.Domain references '{name}'."));
    }
}
