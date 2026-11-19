using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Streaming.Infrastructure.Persistence;

namespace Streaming.IntegrationTests;

[Collection(nameof(StreamingDatabaseCollection))]
public sealed class PersistenceTests(StreamingDatabaseFixture database)
{
    [Fact]
    public async Task Migrations_BuildTheModel()
    {
        await using var services = await database.CreateServicesAsync();
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<StreamingDbContext>();

        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
    }
}
