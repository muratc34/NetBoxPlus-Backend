using System.Reflection;
using Shared.Application.Auditing;

namespace Architecture.Tests;

/// <summary>
/// Audited commands are stored as JSON in the audit log, readable by administrators and kept for
/// a long time; a password or token in one would leak there.
/// </summary>
public sealed class AuditedCommandTests
{
    private static readonly string[] SecretWords = ["Password", "Token", "Secret", "Key"];

    [Fact]
    public void AuditedCommands_AreFound()
    {
        Assert.Contains(AuditedCommands(), type => type.FullName == "Auth.Application.Users.AssignRole.AssignRoleCommand");
    }

    [Fact]
    public void AuditedCommands_CarryNoSecrets()
    {
        var offenders = AuditedCommands()
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => SecretWords.Any(word => property.Name.Contains(word, StringComparison.OrdinalIgnoreCase)))
                .Select(property => $"{type.FullName}.{property.Name}"))
            .ToList();

        Assert.True(offenders.Count == 0, $"Audited commands must not carry secrets: {string.Join(", ", offenders)}");
    }

    private static List<Type> AuditedCommands()
        => Directory.EnumerateFiles(AppContext.BaseDirectory, "*.Application.dll")
            .Where(path => !Path.GetFileName(path).StartsWith("Shared.", StringComparison.Ordinal))
            .SelectMany(path => Assembly.LoadFrom(path).GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IAuditedCommand).IsAssignableFrom(type))
            .ToList();
}
