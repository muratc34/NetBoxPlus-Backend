using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.Infrastructure.Persistence.Inbox;
using Shared.Infrastructure.Persistence.Outbox;
using Shared.Infrastructure.Persistence.Retention;

namespace Shared.Infrastructure.Persistence.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class MessageRetentionTests(PostgresFixture postgres)
{
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);
    private static readonly TimeSpan Month = TimeSpan.FromDays(30);
    private static readonly TimeSpan Tick = TimeSpan.FromTicks(10); // one PostgreSQL microsecond

    [Fact]
    public async Task SentOutboxMessages_GoAfterAWeek_UnsentOnesStay()
    {
        await using var services = await postgres.CreateServicesAsync();
        var now = services.Time.GetUtcNow();
        var old = Outbox(processedAt: now - Week - Tick);
        var justAWeek = Outbox(processedAt: now - Week);
        var recent = Outbox(processedAt: now - TimeSpan.FromHours(1));
        var unsent = Outbox(processedAt: null, occurredAt: now - Month * 2);
        var givenUp = Outbox(processedAt: null, occurredAt: now - Month * 2, attempts: 10);
        await Add(services, [old, justAWeek, recent, unsent, givenUp]);

        var deleted = await Service(services).CleanAsync(TestContext.Current.CancellationToken);

        Assert.Equal((1, 0), deleted);
        Assert.Equal(
            new[] { justAWeek.Id, recent.Id, unsent.Id, givenUp.Id }.Order(),
            (await OutboxIds(services)).Order());
    }

    [Fact]
    public async Task InboxRecords_GoAfterAMonth()
    {
        await using var services = await postgres.CreateServicesAsync();
        var now = services.Time.GetUtcNow();
        var old = Inbox(now - Month - Tick);
        var justAMonth = Inbox(now - Month);
        var lastWeek = Inbox(now - Week - Tick);
        await Add(services, [old, justAMonth, lastWeek]);

        var deleted = await Service(services).CleanAsync(TestContext.Current.CancellationToken);

        Assert.Equal((0, 1), deleted);
        Assert.Equal(
            new[] { justAMonth.MessageId, lastWeek.MessageId }.Order(),
            (await InboxIds(services)).Order());
    }

    [Fact]
    public async Task InboxRecords_OfTheSameMessage_GoOnlyWhenOld()
    {
        await using var services = await postgres.CreateServicesAsync();
        var now = services.Time.GetUtcNow();
        var messageId = Guid.CreateVersion7();
        await Add(services, [Inbox(now - Month - Tick, messageId, "First.Handler"), Inbox(now, messageId, "Second.Handler")]);

        await Service(services).CleanAsync(TestContext.Current.CancellationToken);

        await using var scope = services.Scope();
        var left = await scope.ServiceProvider.GetRequiredService<TestDbContext>().InboxMessages
            .AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Second.Handler", Assert.Single(left).Consumer);
    }

    [Fact]
    public async Task MoreRowsThanABatch_AreAllDeletedInOneCleanUp()
    {
        await using var services = await postgres.CreateServicesAsync();
        var now = services.Time.GetUtcNow();
        await Add(services, [.. Enumerable.Range(0, 7).Select(_ => Outbox(processedAt: now - Month))]);
        await Add(services, [.. Enumerable.Range(0, 6).Select(_ => Inbox(now - Month * 2))]);
        var recent = Outbox(processedAt: now);
        await Add(services, [recent]);

        var deleted = await Service(services, new MessageRetentionOptions { BatchSize = 3 }).CleanAsync(TestContext.Current.CancellationToken);

        Assert.Equal((7, 6), deleted);
        Assert.Equal([recent.Id], await OutboxIds(services));
        Assert.Empty(await InboxIds(services));
    }

    [Fact]
    public async Task TheService_CleansShortlyAfterStart_AndThenEveryInterval()
    {
        await using var services = await postgres.CreateServicesAsync();
        var now = services.Time.GetUtcNow();
        await Add(services, [Outbox(processedAt: now - Month)]);
        using var service = Service(services, new MessageRetentionOptions { Interval = TimeSpan.FromHours(1) });

        await service.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await AdvanceUntil(services, TimeSpan.FromSeconds(10), async () => (await OutboxIds(services)).Count == 0);

            // A message sent now is a week old once the clock has moved a week; the next run takes it.
            var sentNow = Outbox(processedAt: services.Time.GetUtcNow());
            await Add(services, [sentNow]);
            services.Time.Advance(Week);
            await AdvanceUntil(services, TimeSpan.FromHours(1), async () => (await OutboxIds(services)).Count == 0);
        }
        finally
        {
            await service.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task TheService_DoesNothing_WhenDisabled()
    {
        await using var services = await postgres.CreateServicesAsync();
        var old = Outbox(processedAt: services.Time.GetUtcNow() - Month);
        await Add(services, [old]);
        using var service = Service(services, new MessageRetentionOptions { Enabled = false });

        await service.StartAsync(TestContext.Current.CancellationToken);
        for (var i = 0; i < 5; i++)
        {
            services.Time.Advance(TimeSpan.FromHours(1));
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        await service.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal([old.Id], await OutboxIds(services));
    }

    [Fact]
    public async Task TheService_KeepsRunning_AfterAFailedCleanUp()
    {
        await using var services = await postgres.CreateServicesAsync();
        await using (var scope = services.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database
                .ExecuteSqlRawAsync("""ALTER TABLE "InboxMessages" RENAME TO "InboxMessagesAway" """, TestContext.Current.CancellationToken);
        }

        await Add(services, [Outbox(processedAt: services.Time.GetUtcNow() - Month)]);
        using var service = Service(services, new MessageRetentionOptions { Interval = TimeSpan.FromHours(1) });
        await service.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            // The first run deletes the outbox row, then fails on the missing inbox table.
            await AdvanceUntil(services, TimeSpan.FromSeconds(10), async () => (await OutboxIds(services)).Count == 0);

            await using (var scope = services.Scope())
            {
                await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database
                    .ExecuteSqlRawAsync("""ALTER TABLE "InboxMessagesAway" RENAME TO "InboxMessages" """, TestContext.Current.CancellationToken);
            }

            await Add(services, [Inbox(services.Time.GetUtcNow() - Month * 2)]);
            await AdvanceUntil(services, TimeSpan.FromHours(1), async () => (await InboxIds(services)).Count == 0);
        }
        finally
        {
            await service.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [InlineData("MessageRetention:ProcessedOutbox", "00:00:00")]
    [InlineData("MessageRetention:Inbox", "-1.00:00:00")]
    [InlineData("MessageRetention:Interval", "00:00:00")]
    [InlineData("MessageRetention:BatchSize", "0")]
    public void InvalidSettings_AreRejected(string key, string value)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
            .Build());
        services.AddPersistence<TestDbContext>("Host=unused");
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<MessageRetentionOptions>>().Value);
    }

    [Fact]
    public void EveryServiceWithPersistence_RunsTheCleanUp()
    {
        var services = new ServiceCollection();
        services.AddPersistence<TestDbContext>("Host=unused");

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(MessageRetentionService<TestDbContext>));
    }

    [Fact]
    public void TheDefaults_AreAWeekForTheOutboxAndAMonthForTheInbox()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddPersistence<TestDbContext>("Host=unused");
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<MessageRetentionOptions>>().Value;
        Assert.Equal((true, Week, Month, TimeSpan.FromHours(1), 1000), (options.Enabled, options.ProcessedOutbox, options.Inbox, options.Interval, options.BatchSize));
    }

    /// <summary>Moves the fake clock by <paramref name="step"/> until the condition holds, or fails after a while.</summary>
    private static async Task AdvanceUntil(TestServices services, TimeSpan step, Func<Task<bool>> condition)
    {
        for (var i = 0; i < 100; i++)
        {
            services.Time.Advance(step);
            await Task.Delay(50, TestContext.Current.CancellationToken);
            if (await condition())
            {
                return;
            }
        }

        Assert.Fail("The clean-up did not happen.");
    }

    private static MessageRetentionService<TestDbContext> Service(TestServices services, MessageRetentionOptions? options = null) => new(
        services.Provider.GetRequiredService<IServiceScopeFactory>(),
        Options.Create(options ?? new MessageRetentionOptions()),
        services.Time,
        NullLogger<MessageRetentionService<TestDbContext>>.Instance);

    private static OutboxMessage Outbox(DateTimeOffset? processedAt, DateTimeOffset? occurredAt = null, int attempts = 0) => new()
    {
        Id = Guid.CreateVersion7(),
        Type = "Test.Event",
        Payload = "{}",
        OccurredAt = occurredAt ?? processedAt ?? DateTimeOffset.UnixEpoch,
        ProcessedAt = processedAt,
        Attempts = attempts,
    };

    private static InboxMessage Inbox(DateTimeOffset processedAt, Guid? messageId = null, string consumer = "Test.Handler") => new()
    {
        MessageId = messageId ?? Guid.CreateVersion7(),
        Consumer = consumer,
        ProcessedAt = processedAt,
    };

    private static async Task Add(TestServices services, IEnumerable<object> rows)
    {
        await using var scope = services.Scope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        context.AddRange(rows);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<List<Guid>> OutboxIds(TestServices services)
    {
        await using var scope = services.Scope();
        return await scope.ServiceProvider.GetRequiredService<TestDbContext>().OutboxMessages
            .Select(message => message.Id).ToListAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<List<Guid>> InboxIds(TestServices services)
    {
        await using var scope = services.Scope();
        return await scope.ServiceProvider.GetRequiredService<TestDbContext>().InboxMessages
            .Select(message => message.MessageId).ToListAsync(TestContext.Current.CancellationToken);
    }
}
