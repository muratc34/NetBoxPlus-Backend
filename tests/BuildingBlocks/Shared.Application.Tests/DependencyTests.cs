namespace Shared.Application.Tests;

/// <summary>
/// Shared.Application may depend only on the base class library, Shared.Domain and the
/// Microsoft.Extensions abstraction packages — never on an infrastructure library.
/// </summary>
public sealed class DependencyTests
{
    private static readonly string[] AllowedAssemblies =
    [
        "netstandard",
        "Shared.Domain",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        "Microsoft.Extensions.Logging.Abstractions",
    ];

    [Fact]
    public void SharedApplication_ReferencesOnlyAllowedAssemblies()
    {
        var references = typeof(IUnitOfWork).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .ToList();

        Assert.All(references, name => Assert.True(
            name.StartsWith("System", StringComparison.Ordinal) || AllowedAssemblies.Contains(name),
            $"Shared.Application references '{name}'."));
    }
}
