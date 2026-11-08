using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application;

namespace Shared.Infrastructure.Persistence.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class UnitOfWorkTests(PostgresFixture postgres)
{
    [Fact]
    public async Task DuplicateOfAUniqueValue_IsADataConflict()
    {
        await using var services = await postgres.CreateServicesAsync();
        await SaveAsync(services, context => context.Products.Add(Product.Create("Unique", 5)));

        await Assert.ThrowsAsync<DataConflictException>(() => SaveAsync(services, context => context.Products.Add(Product.Create("Unique", 5))));
    }

    [Fact]
    public async Task OtherDatabaseFailures_StayWhatTheyAre()
    {
        await using var services = await postgres.CreateServicesAsync();

        // Longer than the column: not something a retry or a reload would solve.
        var failure = await Assert.ThrowsAsync<DbUpdateException>(
            () => SaveAsync(services, context => context.Products.Add(Product.Create(new string('a', 201), 5))));

        Assert.IsNotType<DbUpdateConcurrencyException>(failure);
    }

    private static async Task SaveAsync(TestServices services, Action<TestDbContext> change)
    {
        await using var scope = services.Scope();
        change(scope.ServiceProvider.GetRequiredService<TestDbContext>());
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
