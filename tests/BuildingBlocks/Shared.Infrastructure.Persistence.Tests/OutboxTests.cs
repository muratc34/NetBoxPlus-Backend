using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.Application.Events;
using Shared.Application.Messaging;
using Shared.Infrastructure.Persistence.Outbox;

namespace Shared.Infrastructure.Persistence.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class OutboxTests(PostgresFixture postgres)
{
    private static readonly OutboxOptions Options = new() { BatchSize = 50, MaxAttempts = 3 };

    [Fact]
    public async Task PublishedEvent_IsStoredWhenTheUnitOfWorkSaves()
    {
        await using var services = await postgres.CreateServicesAsync();
        var productId = Guid.CreateVersion7();

        await using (var scope = services.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>()
                .PublishAsync(new PriceChanged(productId, 12.5m), TestContext.Current.CancellationToken);
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var message = Assert.Single(await Messages(services));
        Assert.Equal(IntegrationEvents.MessageTypeOf(typeof(PriceChanged)), message.Type);
        Assert.Equal(services.Time.GetUtcNow(), message.OccurredAt);
        Assert.Equal(
            new PriceChanged(productId, 12.5m),
            JsonSerializer.Deserialize<PriceChanged>(message.Payload, IntegrationEvents.SerializerOptions));
    }

    [Fact]
    public async Task PublishedEvent_IsNotStoredWithoutASave()
    {
        await using var services = await postgres.CreateServicesAsync();

        await using (var scope = services.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>()
                .PublishAsync(new PriceChanged(Guid.CreateVersion7(), 1), TestContext.Current.CancellationToken);
        }

        Assert.Empty(await Messages(services));
    }

    [Fact]
    public async Task PublishedEvent_IsGoneWhenTheTransactionRollsBack()
    {
        await using var services = await postgres.CreateServicesAsync();

        await using (var scope = services.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await using var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

            context.Products.Add(Product.Create("Book", 10));
            await scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>()
                .PublishAsync(new PriceChanged(Guid.CreateVersion7(), 1), TestContext.Current.CancellationToken);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        }

        Assert.Empty(await Messages(services));
    }

    [Fact]
    public async Task PublishedEvent_KeepsTheCurrentTraceContext()
    {
        await using var services = await postgres.CreateServicesAsync();
        using var listener = ListenToEverything();
        using var source = new ActivitySource("outbox-tests");
        using var request = source.StartActivity("request")!;

        await using (var scope = services.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>()
                .PublishAsync(new PriceChanged(Guid.CreateVersion7(), 1), TestContext.Current.CancellationToken);
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(request.Id, Assert.Single(await Messages(services)).TraceParent);
    }

    [Fact]
    public async Task Processor_SendsMessagesOldestFirstAndMarksThemProcessed()
    {
        await using var services = await postgres.CreateServicesAsync();
        await Publish(services, 1m, 2m, 3m);

        var sent = await Processor(services).ProcessBatchAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, sent);
        Assert.Equal(
            [1m, 2m, 3m],
            services.Transport.Sent.Select(e => JsonSerializer.Deserialize<PriceChanged>(e.Payload, IntegrationEvents.SerializerOptions)!.Amount));
        Assert.All(await Messages(services), m => Assert.NotNull(m.ProcessedAt));
        Assert.Equal(0, await Processor(services).ProcessBatchAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Processor_RecordsFailuresAndGivesUpAfterMaxAttempts()
    {
        await using var services = await postgres.CreateServicesAsync();
        await Publish(services, 1m);
        services.Transport.FailWith = new InvalidOperationException("broker down");

        for (var attempt = 0; attempt < Options.MaxAttempts; attempt++)
        {
            Assert.Equal(1, await Processor(services).ProcessBatchAsync(TestContext.Current.CancellationToken));
        }

        var message = Assert.Single(await Messages(services));
        Assert.Equal(Options.MaxAttempts, message.Attempts);
        Assert.Equal("broker down", message.LastError);
        Assert.Null(message.ProcessedAt);
        Assert.Equal(0, await Processor(services).ProcessBatchAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConcurrentProcessors_NeverSendTheSameMessageTwice()
    {
        await using var services = await postgres.CreateServicesAsync();
        await Publish(services, 1m, 2m, 3m);
        services.Transport.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // The first processor locks all three rows and blocks on its first send.
        var first = Processor(services).ProcessBatchAsync(TestContext.Current.CancellationToken);
        await services.Transport.FirstSendStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // A second processor running meanwhile finds every row locked and skips them.
        var secondSent = await Processor(services).ProcessBatchAsync(TestContext.Current.CancellationToken);

        services.Transport.Gate.SetResult();
        var firstSent = await first;

        Assert.Equal(0, secondSent);
        Assert.Equal(3, firstSent);
        Assert.Equal(3, services.Transport.Sent.Select(e => e.MessageId).Distinct().Count());
    }

    private static OutboxProcessor<TestDbContext> Processor(TestServices services) => new(
        services.Provider.GetRequiredService<IServiceScopeFactory>(),
        Microsoft.Extensions.Options.Options.Create(Options),
        services.Time,
        NullLogger<OutboxProcessor<TestDbContext>>.Instance);

    private static async Task Publish(TestServices services, params decimal[] amounts)
    {
        foreach (var amount in amounts)
        {
            await using var scope = services.Scope();
            await scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>()
                .PublishAsync(new PriceChanged(Guid.CreateVersion7(), amount), TestContext.Current.CancellationToken);
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().SaveChangesAsync(TestContext.Current.CancellationToken);
            services.Time.Advance(TimeSpan.FromSeconds(1));
        }
    }

    private static async Task<List<OutboxMessage>> Messages(TestServices services)
    {
        await using var scope = services.Scope();
        return await scope.ServiceProvider.GetRequiredService<TestDbContext>().OutboxMessages
            .AsNoTracking()
            .OrderBy(m => m.OccurredAt)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    private static ActivityListener ListenToEverything()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
