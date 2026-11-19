using System.Reflection;

namespace Architecture.Tests;

/// <summary>
/// The gateway takes only the HTTP building block: no database or broker code in its image, and no
/// service code, so it cannot grow business logic.
/// </summary>
public sealed class GatewayDependencyTests
{
    [Fact]
    public void Gateway_ReferencesNoOtherProjectThanTheHttpBuildingBlock()
    {
        var gateway = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Gateway.dll"));

        // Shared.Infrastructure.API brings Shared.Application and Shared.Domain along; those are fine.
        string[] allowed = ["Shared.Infrastructure.API", "Shared.Application", "Shared.Domain"];
        var projects = gateway.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => File.Exists(Path.Combine(AppContext.BaseDirectory, name + ".dll")) && IsOwnProject(name));

        Assert.All(projects, project => Assert.Contains(project, allowed));
    }

    // Projects of this repository, as opposed to packages: services are "<Service>.<Layer>" and the
    // building blocks "Shared.*".
    private static bool IsOwnProject(string name)
        => name.StartsWith("Shared.", StringComparison.Ordinal)
            || name.EndsWith(".Domain", StringComparison.Ordinal)
            || name.EndsWith(".Application", StringComparison.Ordinal)
            || name.EndsWith(".Infrastructure", StringComparison.Ordinal)
            || name.EndsWith(".API", StringComparison.Ordinal)
            || name.EndsWith(".Contracts", StringComparison.Ordinal);
}
