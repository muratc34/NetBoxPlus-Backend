using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Architecture.Tests;

/// <summary>
/// Every service's model matches its last migration. The shared tables (outbox, inbox, audit log)
/// live in each service's database, so a change to them needs a migration in every service; this
/// test is what notices a forgotten one.
/// </summary>
public sealed class MigrationTests
{
    private static readonly string[] Services = ["Auth", "Billing", "Catalog", "Notification", "Streaming"];

    public static TheoryData<string> ServiceNames() => [.. Services];

    [Theory]
    [MemberData(nameof(ServiceNames))]
    public void TheModel_HasNoChangesWithoutAMigration(string service)
    {
        var assembly = Assembly.Load($"{service}.Infrastructure");
        var factoryType = assembly.GetTypes().Single(type => type.GetInterfaces().Any(i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDesignTimeDbContextFactory<>)));
        var factory = Activator.CreateInstance(factoryType, nonPublic: true)!;
        var create = factoryType.GetMethod("CreateDbContext")!;

        using var context = (DbContext)create.Invoke(factory, [Array.Empty<string>()])!;

        Assert.False(context.Database.HasPendingModelChanges(), $"{service}'s model has changes without a migration.");
    }
}
