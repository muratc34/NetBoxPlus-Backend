using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notification.Infrastructure.Persistence;

namespace Notification.IntegrationTests;

[Collection(nameof(NotificationDatabaseCollection))]
public sealed class PersistenceTests(NotificationDatabaseFixture database)
{
    [Fact]
    public async Task Migrations_BuildTheModel()
    {
        await using var services = await database.CreateServicesAsync();
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();

        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
    }
}
