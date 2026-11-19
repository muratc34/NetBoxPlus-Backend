using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared.Application.Messaging;
using Shared.Domain;

namespace Shared.Infrastructure.Persistence.Inbox;

internal sealed class EfInbox<TContext>(TContext context, TimeProvider timeProvider) : IInbox
    where TContext : ServiceDbContext
{
    public async Task<bool> ProcessOnceAsync(
        Guid messageId,
        string consumer,
        Func<CancellationToken, Task<Result>> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);
        ArgumentNullException.ThrowIfNull(work);

        var alreadyProcessed = await context.InboxMessages
            .AnyAsync(m => m.MessageId == messageId && m.Consumer == consumer, cancellationToken)
            .ConfigureAwait(false);

        if (alreadyProcessed)
        {
            return false;
        }

        // Tracked before the work runs: if the work saves (for example through a command), the
        // inbox row is committed with it.
        var record = new InboxMessage { MessageId = messageId, Consumer = consumer, ProcessedAt = timeProvider.GetUtcNow() };
        context.InboxMessages.Add(record);

        try
        {
            var result = await work(cancellationToken).ConfigureAwait(false);

            if (result.IsFailure)
            {
                // Retrying would fail the same way: keep the record, drop the work's partial changes.
                context.ChangeTracker.Clear();
                context.InboxMessages.Add(record);
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException exception) when (IsDuplicateRecord(exception))
        {
            // A concurrent delivery of the same message won the race.
            context.ChangeTracker.Clear();
            return false;
        }
    }

    // Only a violation of the inbox key means "already processed"; any other unique violation is the
    // handler's own problem and must surface.
    private static bool IsDuplicateRecord(DbUpdateException exception)
        => exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: InboxMessageConfiguration.PrimaryKeyName,
        };
}
