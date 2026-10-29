using System.Reflection;
using System.Text.RegularExpressions;

namespace Architecture.Tests;

/// <summary>
/// Dependency rules for every service project found next to this test (its csproj references all
/// of them). In a monorepo nothing physically stops one service from using another's code; these
/// tests do.
/// </summary>
public sealed partial class ServiceDependencyTests
{
    public static TheoryData<string> ServiceAssemblies()
    {
        var data = new TheoryData<string>();
        foreach (var name in DiscoverServiceAssemblies().Keys.Order(StringComparer.Ordinal))
        {
            data.Add(name);
        }

        return data;
    }

    [Fact]
    public void ServiceProjects_AreFound()
    {
        Assert.Contains("Sample.Domain", DiscoverServiceAssemblies().Keys);
    }

    [Theory]
    [MemberData(nameof(ServiceAssemblies))]
    public void Service_ReferencesOtherServicesOnlyThroughTheirContracts(string assemblyName)
    {
        var (service, _) = Split(assemblyName);

        foreach (var reference in References(assemblyName))
        {
            if (ServiceAssemblyPattern().Match(reference) is { Success: true } match && match.Groups["service"].Value != service)
            {
                Assert.True(
                    match.Groups["layer"].Value == "Contracts",
                    $"{assemblyName} references {reference}; another service is reachable only through its Contracts project.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(ServiceAssemblies))]
    public void Layer_DependsOnlyOnLayersBelowIt(string assemblyName)
    {
        var (service, layer) = Split(assemblyName);
        var allowed = layer switch
        {
            "Contracts" => Array.Empty<string>(),
            "Domain" => [],
            "Application" => ["Domain", "Contracts"],
            "Infrastructure" => ["Application", "Domain", "Contracts"],
            "API" => ["Infrastructure", "Application", "Domain", "Contracts"],
            "Migrator" => ["Infrastructure", "Application", "Domain", "Contracts"],
            _ => throw new InvalidOperationException($"Unknown layer {layer}."),
        };

        var ownLayers = References(assemblyName)
            .Select(reference => ServiceAssemblyPattern().Match(reference))
            .Where(match => match.Success && match.Groups["service"].Value == service)
            .Select(match => match.Groups["layer"].Value);

        Assert.All(ownLayers, referenced => Assert.True(
            allowed.Contains(referenced),
            $"{assemblyName} must not reference {service}.{referenced}."));
    }

    [Theory]
    [MemberData(nameof(ServiceAssemblies))]
    public void ContractsAndDomain_StayFreeOfInfrastructure(string assemblyName)
    {
        var (_, layer) = Split(assemblyName);
        if (layer is not ("Contracts" or "Domain"))
        {
            return;
        }

        string[] allowedPrefixes = layer == "Domain" ? ["System", "netstandard", "Shared.Domain"] : ["System", "netstandard"];

        Assert.All(References(assemblyName), reference => Assert.True(
            allowedPrefixes.Any(prefix => reference.StartsWith(prefix, StringComparison.Ordinal)),
            $"{assemblyName} references {reference}."));
    }

    private static Dictionary<string, string> DiscoverServiceAssemblies()
        => Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Where(name => ServiceAssemblyPattern().IsMatch(name))
            .ToDictionary(name => name, name => Path.Combine(AppContext.BaseDirectory, name + ".dll"), StringComparer.Ordinal);

    private static List<string> References(string assemblyName)
        => Assembly.LoadFrom(DiscoverServiceAssemblies()[assemblyName]).GetReferencedAssemblies().Select(a => a.Name!).ToList();

    private static (string Service, string Layer) Split(string assemblyName)
    {
        var match = ServiceAssemblyPattern().Match(assemblyName);
        return (match.Groups["service"].Value, match.Groups["layer"].Value);
    }

    // "<Service>.<Layer>", excluding the shared building blocks.
    [GeneratedRegex("^(?!Shared\\.)(?<service>[A-Z][A-Za-z0-9]*)\\.(?<layer>Domain|Application|Infrastructure|API|Contracts|Migrator)$")]
    private static partial Regex ServiceAssemblyPattern();
}
