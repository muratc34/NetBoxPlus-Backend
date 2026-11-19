using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared.Application;

namespace Shared.Infrastructure.Persistence;

internal sealed class EfUnitOfWork<TContext>(TContext context) : IUnitOfWork
    where TContext : ServiceDbContext
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsConflict(exception))
        {
            throw new DataConflictException(exception);
        }
    }

    private static bool IsConflict(DbUpdateException exception)
        => exception is DbUpdateConcurrencyException
            || exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation };
}
