using System.Xml.Linq;

namespace Architecture.Tests;

/// <summary>
/// The same rule as <see cref="ServiceDependencyTests"/>, read from the project files. The compiler
/// leaves a referenced assembly out when no code uses it, so the assembly-based tests miss a project
/// reference that is declared but not used yet; this catches it before the first line of code does.
/// </summary>
public sealed class ProjectReferenceTests
{
    public static TheoryData<string> ServiceProjects()
    {
        var data = new TheoryData<string>();
        foreach (var project in Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "src", "Services"), "*.csproj", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(RepositoryRoot, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal))
        {
            data.Add(project);
        }

        return data;
    }

    [Fact]
    public void ServiceProjects_AreFound()
    {
        Assert.Contains("src/Services/Sample/Sample.Domain/Sample.Domain.csproj", ServiceProjects().Select(row => row.Data));
    }

    [Theory]
    [MemberData(nameof(ServiceProjects))]
    public void Service_ReferencesOtherServicesOnlyThroughTheirContracts(string project)
    {
        var path = Path.Combine(RepositoryRoot, project);
        var service = ServiceOf(path);

        var references = XDocument.Load(path).Descendants("ProjectReference")
            .Select(reference => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, reference.Attribute("Include")!.Value)));

        foreach (var reference in references)
        {
            if (ServiceOf(reference) is { } other && other != service)
            {
                Assert.True(
                    Path.GetFileNameWithoutExtension(reference).EndsWith(".Contracts", StringComparison.Ordinal),
                    $"{project} references {Path.GetFileName(reference)}; another service is reachable only through its Contracts project.");
            }
        }
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    // The service a project belongs to: the folder right under src/Services, or null outside it.
    private static string? ServiceOf(string projectPath)
    {
        var relative = Path.GetRelativePath(Path.Combine(RepositoryRoot, "src", "Services"), projectPath).Replace('\\', '/');
        return relative.StartsWith("../", StringComparison.Ordinal) ? null : relative.Split('/')[0];
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NetBoxPlus.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("NetBoxPlus.slnx was not found above the test's output directory.");
    }
}
