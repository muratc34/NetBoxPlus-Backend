using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Application.Messaging;

namespace Shared.Infrastructure.Persistence.Outbox;

/// <summary>
/// Sends outbox messages to the broker. Each batch is locked with <c>FOR UPDATE SKIP LOCKED</c>
/// inside a transaction, so several replicas can run side by side without sending a message twice.
/// Messages are sent oldest first, but consumers must not rely on ordering across batches or replicas.
/// </summary>
internal sealed partial class OutboxProcessor<TContext>(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxProcessor<TContext>> logger)
    : BackgroundService
    where TContext : ServiceDbContext
{
    private const int MaxErrorLength = 2000;

    private readonly OutboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            int sent;
            try
            {
                sent = await ProcessBatchAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogBatchFailed(logger, exception);
                sent = 0;
            }

            if (sent < _options.BatchSize)
            {
                try
                {
                    await Task.Delay(_options.PollingInterval, timeProvider, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <summary>Sends one batch and returns how many messages it contained.</summary>
    internal async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var messages = await context.OutboxMessages
            .FromSqlRaw(LockBatchSql(context), _options.MaxAttempts, _options.BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (messages.Count == 0)
        {
            return 0;
        }

        var transport = scope.ServiceProvider.GetRequiredService<IMessageTransport>();

        foreach (var message in messages)
        {
            try
            {
                await transport.SendAsync(
                    new MessageEnvelope(message.Id, message.Type, message.OccurredAt, message.TraceParent, message.Payload),
                    cancellationToken).ConfigureAwait(false);

                message.ProcessedAt = timeProvider.GetUtcNow();
                message.LastError = null;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                message.Attempts++;
                message.LastError = exception.Message.Length > MaxErrorLength ? exception.Message[..MaxErrorLength] : exception.Message;

                if (message.Attempts >= _options.MaxAttempts)
                {
                    LogGaveUp(logger, exception, message.Id, message.Type, message.Attempts);
                }
                else
                {
                    LogSendFailed(logger, exception, message.Id, message.Type, message.Attempts);
                }
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return messages.Count;
    }

    // Identifiers come from the EF model, so the query follows whatever table name or schema the
    // service's model ends up with; only the two numbers are parameters.
    internal static string LockBatchSql(DbContext context)
    {
        var entityType = context.Model.FindEntityType(typeof(OutboxMessage))!;
        var table = StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema());
        var sql = context.GetService<ISqlGenerationHelper>();

        string Column(string property) => sql.DelimitIdentifier(entityType.FindProperty(property)!.GetColumnName(table)!);

        // {0} and {1} are FromSqlRaw parameter placeholders, not interpolation holes.
        return $$"""
            SELECT * FROM {{sql.DelimitIdentifier(table.Name, table.Schema)}}
            WHERE {{Column(nameof(OutboxMessage.ProcessedAt))}} IS NULL
              AND {{Column(nameof(OutboxMessage.Attempts))}} < {0}
            ORDER BY {{Column(nameof(OutboxMessage.OccurredAt))}}
            LIMIT {1}
            FOR UPDATE SKIP LOCKED
            """;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox batch failed")]
    private static partial void LogBatchFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sending outbox message {MessageId} ({MessageType}) failed, attempt {Attempts}")]
    private static partial void LogSendFailed(ILogger logger, Exception exception, Guid messageId, string messageType, int attempts);

    [LoggerMessage(Level = LogLevel.Error, Message = "Giving up on outbox message {MessageId} ({MessageType}) after {Attempts} attempts")]
    private static partial void LogGaveUp(ILogger logger, Exception exception, Guid messageId, string messageType, int attempts);
}
