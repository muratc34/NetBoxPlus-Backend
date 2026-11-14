using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Infrastructure.Persistence.Inbox;
using Shared.Infrastructure.Persistence.Outbox;

namespace Shared.Infrastructure.Persistence.Retention;

/// <summary>
/// Deletes sent outbox messages and old inbox records, shortly after start and then every
/// <see cref="MessageRetentionOptions.Interval"/>. Rows go in small batches so the tables are never
/// locked for long. Deleting is idempotent, so replicas can run it side by side without coordination.
/// Unsent outbox messages, including the ones the processor gave up on, are kept: they need attention.
/// </summary>
internal sealed partial class MessageRetentionService<TContext>(
    IServiceScopeFactory scopeFactory,
    IOptions<MessageRetentionOptions> options,
    TimeProvider timeProvider,
    ILogger<MessageRetentionService<TContext>> logger)
    : BackgroundService
    where TContext : ServiceDbContext
{
    /// <summary>Lets the service finish starting before the first clean-up.</summary>
    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(10);

    private readonly MessageRetentionOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var delay = FirstDelay;
        while (true)
        {
            try
            {
                await Task.Delay(delay, timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await CleanAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogCleanupFailed(logger, exception);
            }

            delay = _options.Interval;
        }
    }

    /// <summary>Runs one clean-up and returns how many outbox and inbox rows it deleted.</summary>
    internal async Task<(int Outbox, int Inbox)> CleanAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        var now = timeProvider.GetUtcNow();

        var outbox = await DeleteAllAsync(context, DeleteBatchSql<OutboxMessage>(context, nameof(OutboxMessage.ProcessedAt)), now - _options.ProcessedOutbox, cancellationToken).ConfigureAwait(false);
        var inbox = await DeleteAllAsync(context, DeleteBatchSql<InboxMessage>(context, nameof(InboxMessage.ProcessedAt)), now - _options.Inbox, cancellationToken).ConfigureAwait(false);

        if (outbox > 0 || inbox > 0)
        {
            LogCleaned(logger, outbox, inbox);
        }

        return (outbox, inbox);
    }

    private async Task<int> DeleteAllAsync(TContext context, string sql, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var total = 0;
        int deleted;
        do
        {
            deleted = await context.Database.ExecuteSqlRawAsync(sql, [cutoff, _options.BatchSize], cancellationToken).ConfigureAwait(false);
            total += deleted;
        }
        while (deleted >= _options.BatchSize);

        return total;
    }

    // Identifiers come from the EF model, as in the outbox processor; only the cutoff and the batch
    // size are parameters. ctid addresses rows whatever the key looks like (the inbox key is composite).
    // A null ProcessedAt (an unsent outbox message) never compares as older than the cutoff.
    internal static string DeleteBatchSql<TEntity>(DbContext context, string processedAtProperty)
    {
        var entityType = context.Model.FindEntityType(typeof(TEntity))!;
        var table = StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema());
        var sql = context.GetService<ISqlGenerationHelper>();
        var tableName = sql.DelimitIdentifier(table.Name, table.Schema);
        var processedAt = sql.DelimitIdentifier(entityType.FindProperty(processedAtProperty)!.GetColumnName(table)!);

        // {0} and {1} are ExecuteSqlRaw parameter placeholders, not interpolation holes.
        return $$"""
            DELETE FROM {{tableName}}
            WHERE ctid IN (
                SELECT ctid FROM {{tableName}}
                WHERE {{processedAt}} < {0}
                LIMIT {1})
            """;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {OutboxCount} sent outbox messages and {InboxCount} inbox records past their retention")]
    private static partial void LogCleaned(ILogger logger, int outboxCount, int inboxCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Deleting old outbox and inbox rows failed; trying again at the next clean-up")]
    private static partial void LogCleanupFailed(ILogger logger, Exception exception);
}
