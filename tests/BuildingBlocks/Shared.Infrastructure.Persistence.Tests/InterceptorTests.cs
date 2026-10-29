using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Shared.Infrastructure.Persistence.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class InterceptorTests(PostgresFixture postgres)
{
    [Fact]
    public async Task NewEntity_GetsCreatedAt()
    {
        await using var services = await postgres.CreateServicesAsync();
        var product = Product.Create("Book", 10);

        await using (var scope = services.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            context.Products.Add(product);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var saved = await Load(services, product.Id);
        Assert.Equal(services.Time.GetUtcNow(), saved.CreatedAt);
        Assert.Null(saved.ModifiedAt);
    }

    [Fact]
    public async Task ChangedEntity_GetsModifiedAt()
    {
        await using var services = await postgres.CreateServicesAsync();
        var product = await Seed(services, "Book");
        services.Time.Advance(TimeSpan.FromMinutes(5));

        await using (var scope = services.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var tracked = await context.Products.SingleAsync(p => p.Id == product.Id, TestContext.Current.CancellationToken);
            tracked.Rename("Novel");
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var saved = await Load(services, product.Id);
        Assert.Equal(services.Time.GetUtcNow(), saved.ModifiedAt);
        Assert.Equal(services.Time.GetUtcNow() - TimeSpan.FromMinutes(5), saved.CreatedAt);
    }

    [Fact]
    public async Task DeletedEntity_IsSoftDeletedAndKeepsItsOwnedValues()
    {
        await using var services = await postgres.CreateServicesAsync();
        var product = await Seed(services, "Book");

        await using (var scope = services.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var tracked = await context.Products.SingleAsync(p => p.Id == product.Id, TestContext.Current.CancellationToken);
            context.Products.Remove(tracked);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var saved = await Load(services, product.Id);
        Assert.True(saved.IsDeleted);
        Assert.Equal(services.Time.GetUtcNow(), saved.DeletedAt);
        Assert.Equal(new Money(10, "TRY"), saved.Price);
    }

    [Fact]
    public async Task DomainEvents_AreHandledInTheSameSave()
    {
        await using var services = await postgres.CreateServicesAsync();
        var product = Product.Create("Book", 10);

        await using (var scope = services.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            context.Products.Add(product);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            Assert.Empty(product.DomainEvents);
        }

        await using var check = services.Scope();
        var log = await check.ServiceProvider.GetRequiredService<TestDbContext>().ProductLogs
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(product.Id, log.ProductId);
        Assert.Equal("created Book", log.Message);
    }

    [Fact]
    public async Task SynchronousSave_WithPendingDomainEvents_IsRejected()
    {
        await using var services = await postgres.CreateServicesAsync();
        await using var scope = services.Scope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        context.Products.Add(Product.Create("Book", 10));

        Assert.Throws<NotSupportedException>(() => context.SaveChanges());
    }

    private static async Task<Product> Seed(TestServices services, string name)
    {
        var product = Product.Create(name, 10);
        await using var scope = services.Scope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        context.Products.Add(product);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return product;
    }

    private static async Task<Product> Load(TestServices services, Guid id)
    {
        await using var scope = services.Scope();
        return await scope.ServiceProvider.GetRequiredService<TestDbContext>().Products
            .AsNoTracking()
            .SingleAsync(p => p.Id == id, TestContext.Current.CancellationToken);
    }
}
