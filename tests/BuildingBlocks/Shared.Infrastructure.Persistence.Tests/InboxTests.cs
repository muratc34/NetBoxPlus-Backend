using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Messaging;
using Shared.Domain;
using Shared.Infrastructure.Persistence.Inbox;

namespace Shared.Infrastructure.Persistence.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class InboxTests(PostgresFixture postgres)
{
    private const string Consumer = "Tests.ProductImporter";

    [Fact]
    public async Task FirstDelivery_RunsTheWorkAndSavesItWithTheRecord()
    {
        await using var services = await postgres.CreateServicesAsync();
        var messageId = Guid.CreateVersion7();

        var processed = await ProcessAsync(services, messageId, context =>
        {
            context.Products.Add(Product.Create("Imported", 5));
            return Result.Success();
        });

        Assert.True(processed);
        Assert.Equal(1, await Count<Product>(services));
        Assert.Equal(1, await Count<InboxMessage>(services));
    }

    [Fact]
    public async Task SecondDelivery_IsSkipped()
    {
        await using var services = await postgres.CreateServicesAsync();
        var messageId = Guid.CreateVersion7();
        var runs = 0;

        await ProcessAsync(services, messageId, _ => { runs++; return Result.Success(); });
        var second = await ProcessAsync(services, messageId, _ => { runs++; return Result.Success(); });

        Assert.False(second);
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task SameMessage_ForAnotherConsumer_IsProcessedAgain()
    {
        await using var services = await postgres.CreateServicesAsync();
        var messageId = Guid.CreateVersion7();

        await ProcessAsync(services, messageId, _ => Result.Success());
        var other = await ProcessAsync(services, messageId, _ => Result.Success(), consumer: "Tests.OtherHandler");

        Assert.True(other);
    }

    [Fact]
    public async Task FailedResult_RecordsTheMessageButDiscardsTheWork()
    {
        await using var services = await postgres.CreateServicesAsync();
        var messageId = Guid.CreateVersion7();

        var processed = await ProcessAsync(services, messageId, context =>
        {
            context.Products.Add(Product.Create("Half done", 5));
            return Error.Conflict("Product.Exists", "The product already exists.");
        });

        Assert.True(processed);
        Assert.Equal(0, await Count<Product>(services));
        Assert.Equal(1, await Count<InboxMessage>(services));
    }

    [Fact]
    public async Task ThrowingWork_RecordsNothing()
    {
        await using var services = await postgres.CreateServicesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessAsync(
            services,
            Guid.CreateVersion7(),
            _ => throw new InvalidOperationException("transient")));

        Assert.Equal(0, await Count<InboxMessage>(services));
    }

    [Fact]
    public async Task ConcurrentDuplicate_IsDetectedByTheInboxKey()
    {
        await using var services = await postgres.CreateServicesAsync();
        var messageId = Guid.CreateVersion7();

        // While this delivery works, another delivery of the same message commits first.
        var processed = await ProcessAsync(services, messageId, async (context, ct) =>
        {
            context.Products.Add(Product.Create("Loser", 5));
            await ProcessAsync(services, messageId, _ => Result.Success());
            return Result.Success();
        });

        Assert.False(processed);
        Assert.Equal(0, await Count<Product>(services));
        Assert.Equal(1, await Count<InboxMessage>(services));
    }

    [Fact]
    public async Task OtherUniqueViolations_AreNotMistakenForDuplicates()
    {
        await using var services = await postgres.CreateServicesAsync();
        await ProcessAsync(services, Guid.CreateVersion7(), context =>
        {
            context.Products.Add(Product.Create("Unique", 5));
            return Result.Success();
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => ProcessAsync(services, Guid.CreateVersion7(), context =>
        {
            context.Products.Add(Product.Create("Unique", 5));
            return Result.Success();
        }));
    }

    private static Task<bool> ProcessAsync(TestServices services, Guid messageId, Func<TestDbContext, Result> work, string consumer = Consumer)
        => ProcessAsync(services, messageId, (context, _) => Task.FromResult(work(context)), consumer);

    private static async Task<bool> ProcessAsync(
        TestServices services,
        Guid messageId,
        Func<TestDbContext, CancellationToken, Task<Result>> work,
        string consumer = Consumer)
    {
        await using var scope = services.Scope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var inbox = scope.ServiceProvider.GetRequiredService<IInbox>();

        return await inbox.ProcessOnceAsync(messageId, consumer, ct => work(context, ct), TestContext.Current.CancellationToken);
    }

    private static async Task<int> Count<TEntity>(TestServices services)
        where TEntity : class
    {
        await using var scope = services.Scope();
        return await scope.ServiceProvider.GetRequiredService<TestDbContext>().Set<TEntity>().CountAsync(TestContext.Current.CancellationToken);
    }
}
