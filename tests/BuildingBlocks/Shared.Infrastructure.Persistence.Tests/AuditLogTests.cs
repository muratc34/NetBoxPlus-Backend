using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Auditing;
using Shared.Infrastructure.Persistence.Auditing;

namespace Shared.Infrastructure.Persistence.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class AuditLogTests(PostgresFixture postgres)
{
    private sealed record RenameProduct(Guid ProductId, string Name);

    [Fact]
    public async Task Entry_IsStoredWithTheChange_AsJson()
    {
        await using var services = await postgres.CreateServicesAsync();
        var actorId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        using var activity = new Activity("audited request").Start();

        await using (var scope = services.Scope())
        {
            scope.ServiceProvider.GetRequiredService<IAuditLog>()
                .Add(new AuditEntry("RenameProduct", actorId, productId, new RenameProduct(productId, "Renamed")));
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var entry = Assert.Single(await Entries(services));
        Assert.Equal("RenameProduct", entry.Action);
        Assert.Equal(actorId, entry.ActorId);
        Assert.Equal(productId, entry.TargetId);
        Assert.Equal(services.Time.GetUtcNow(), entry.OccurredAt);
        Assert.Equal(activity.TraceId.ToHexString(), entry.TraceId);

        using var details = JsonDocument.Parse(entry.Details);
        Assert.Equal("Renamed", details.RootElement.GetProperty("name").GetString());
        Assert.Equal(productId, details.RootElement.GetProperty("productId").GetGuid());
    }

    [Fact]
    public async Task Entry_IsNotStoredWithoutASave()
    {
        await using var services = await postgres.CreateServicesAsync();

        await using (var scope = services.Scope())
        {
            scope.ServiceProvider.GetRequiredService<IAuditLog>()
                .Add(new AuditEntry("RenameProduct", null, null, new { }));
        }

        Assert.Empty(await Entries(services));
    }

    private static async Task<List<AuditLogEntry>> Entries(TestServices services)
    {
        await using var scope = services.Scope();
        return await scope.ServiceProvider.GetRequiredService<TestDbContext>().AuditLogEntries
            .AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);
    }
}
